# AudioHQ - Technical Architecture

> Keep this file truthful to the code. Update it in the same commit as any
> behavior change it describes (rule: CLAUDE.md "Documentation").
> Last updated: 2026-07-08 (v0.5.7).

## Solution layout

Three projects, strict dependency direction (UI never leaks into the engine):

```
AudioHQ.App (WPF, net7.0-windows)  ──┐
                                     ├──>  AudioHQ.Core (net7.0, NAudio 2.3)
AudioHQ.Cli (console, net7.0)      ──┘
```

- **AudioHQ.Core** - audio engine. No WPF/WinForms references, ever.
- **AudioHQ.App** - WPF GUI, plain MVVM (hand-rolled `ViewModelBase` with
  `INotifyPropertyChanged`, no MVVM framework).
- **AudioHQ.Cli** - minimal console front end used to test the engine without
  the GUI (uses the simpler `LoopbackMirror`, not `MirrorEngine`).

Version is centralized in `Directory.Build.props` (every assembly inherits
it); `AudioHQ.Core.AppVersion` exposes it to both front ends.

## Signal flow (MirrorEngine - the GUI path)

```
source MMDevice (render endpoint)
   │  WASAPI loopback capture (WasapiLoopbackCapture, ~10 ms chunks)
   ▼
MirrorEngine.OnDataAvailable          [capture thread]
   │  lock-free: iterates a published snapshot of the outputs
   ▼  per output:
OutputChannel.Write
   │  safety-net backlog check: BufferedDuration > latency+25ms -> ClearBuffer
   ▼
BufferedWaveProvider (2 s capacity, DiscardOnBufferOverflow)
   │
   ▼
AdaptiveResampler                          capture rate -> device mix rate,
   │                                       ratio nudged to hold backlog at target
   ▼
EqualizerProvider                          per-channel graphic EQ (3/6 peaking
   │                                       biquads + optional low-pass cascade
   │                                       per audio channel); off = bypass
   ▼
VolumeSampleProvider                       gain 0..2, mute = volume 0
   │
   ▼
WasapiOut (shared mode, event-sync; push-mode fallback)  -> physical device
```

Key decisions:

- **Fan-out at the byte level.** One capture feeds N independent per-device
  pipelines; each output owns its buffer, resampler, gain and `WasapiOut`.
  A slow/failed device cannot stall the others (worst case it resyncs).
- **Drift compensation (`AdaptiveResampler`).** The capture clock and each
  output clock run independently, so a fixed-ratio resample lets the backlog
  slowly creep (delay grows, then a hard flush jumps it back - audible on low
  latency). Instead, every output runs through `AdaptiveResampler`: it reads the
  live `BufferedDuration`, smooths it, and steers the resample ratio (a P
  controller, capped at +/-0.5% - inaudible pitch) to hold the backlog at a
  target of `latency + 10 ms` - high enough to cover a full WASAPI pull plus
  capture-chunk jitter (a lower target starves the buffer between pulls and feeds
  silence), low enough to stay under the hard resync. It also performs the base
  capture-rate -> device-rate conversion, so it always runs even when rates match.
- **Backlog resync (safety net).** If a device's queue still exceeds
  `latency + 25 ms` (a stall or large glitch the controller cannot absorb), the
  buffer is cleared. With drift compensation this no longer fires in normal use.
  Logged each time it happens.
- **Per-channel EQ (`EqualizerProvider`).** A bank of NAudio peaking-EQ biquad
  filters (one per band per audio channel) sits between resampling and gain.
  3-band (100 / 1k / 8k Hz) or 6-band (80 / 200 / 500 / 1.2k / 3k / 8k Hz).
  Faders are asymmetric: +12 dB boost, -36 dB cut (`EqBands.MaxBoostDb` /
  `MaxCutDb`), so a band can be taken nearly out of the mix. Each band also has a
  per-band Q (bell width, clamped to `EqBands.QMin..QMax`); `EqSettings.QValues`
  carries it, falling back to the band-count default (`Q3`/`Q6`) when unset.
  Optionally a "bass-only" low-pass runs after the bands: a cascade of
  `BiQuadFilter.LowPassFilter` stages (1 = 12 dB/oct, 2 = 24 dB/oct) at an
  adjustable cutoff (`LowPassMinHz..LowPassMaxHz`) - it passes the deep low end
  and rolls off everything above, the tool a bass shaker wants where a peaking
  bell could only dip. Disabled by default (pure pass-through). The UI
  reconfigures it live; `Configure` updates the existing filters' coefficients IN
  PLACE under a lock when the topology (band count, low-pass stages) is unchanged,
  so the delay-line state survives and dragging a fader never clicks; the bank is
  rebuilt only on a topology change, and all updates are atomic against the audio
  thread's `Read`. EQ state (enable, band count, gains, Q, low-pass) is persisted
  per channel in `settings.json`. The editor draws the response curve as a sum of
  per-band bells whose width follows Q, so it tracks both the gain faders and the
  Q knobs (the low-pass has its own numeric cutoff readout).
- **Push-mode fallback.** Some drivers (notably NVIDIA HDMI) reject
  event-driven shared mode; `OutputChannel` retries with `useEventSync:false`.
- **`LoopbackMirror`** is the milestone-1 single-target version of the same
  pipeline, kept for the CLI tester. If engine behavior changes, prefer
  changing `MirrorEngine`; `LoopbackMirror` may lag behind feature-wise.

## Threading model

- WASAPI capture delivers buffers on NAudio's capture thread.
  `MirrorEngine.OnDataAvailable` runs there and only does: read the outputs
  snapshot, `BufferedWaveProvider.AddSamples` per output. It takes NO lock and
  does no I/O - a UI-thread add/remove or a slow log write can never stall the
  capture callback. Keep this path allocation-light and fast.
- `_lock` in `MirrorEngine` guards mutations of the outputs list; every
  add/remove republishes an immutable snapshot array (volatile) that the capture
  thread iterates. A channel removed concurrently may receive one final `Write`,
  which its disposed flag drops.
- Each `WasapiOut` runs its own render thread reading from the buffered
  provider chain.
- ViewModels touch the engine only from the UI thread (activate/deactivate,
  gain, mute, source/latency change).
- The engine raises two events back to the UI: `MirrorEngine.SourceLost`
  (capture died) and `OutputChannel.PlaybackStopped` (one output died -
  invalidated after sleep/resume, unplugged, disabled; `Dispose` unsubscribes
  first so intentional teardown never fires it). Both can arrive off the UI
  thread; `MixerViewModel.OnEngineSourceLost` and
  `ChannelViewModel.OnPlaybackStopped` marshal through the `Dispatcher` before
  touching view state.

## Device-loss recovery (watchdog, sleep/resume)

The capture source or any output can vanish mid-session (USB dongle unplugged,
device disabled, PC sleep invalidating WASAPI clients). Several mechanisms keep
the app alive and self-healing:

- **Fresh MMDevice policy.** A cached `MMDevice` can be invalidated by
  sleep/resume or unplug/replug even though the endpoint still enumerates fine
  (`AUDCLNT_E_DEVICE_INVALIDATED` on the old COM object). So nothing that opens
  an audio client ever reuses a cached instance: channels persist only the
  endpoint id and resolve a fresh device via `AudioDevices.FindRenderById` at
  every activation (the `OutputChannel` owns and disposes it), and
  `RestartEngine` does the same for the capture source (the engine owns it).
  The instances in `Sources` exist only for the UI (combo box, master volume).
- **Source event path.** `MirrorEngine` subscribes to `RecordingStopped`. An
  unsolicited stop (the handler is detached before an intentional `Stop`) means
  the source endpoint was invalidated: `IsCapturing` goes false and `SourceLost`
  fires. `MixerViewModel.HandleSourceLost` shows a status and calls `TryRecover`.
- **Output event path.** Each `OutputChannel` subscribes to its `WasapiOut`'s
  `PlaybackStopped`. An unsolicited stop raises `OutputChannel.PlaybackStopped`;
  `ChannelViewModel` detaches the dead output, shows `Reconnecting...` and leaves
  the ON intent set so the watchdog brings the channel back.
- **ON intent (`WantsActive`).** Each channel separates "the user wants this ON"
  (persisted as `ChannelDefinition.Active`) from "it is currently running". Only
  an explicit user toggle changes the intent; mechanical stops (device loss,
  engine restart, sleep, becoming the source) go through `Suspend()` and keep it.
  Every watchdog tick, `TryAutoReactivate` re-opens wanted-but-inactive channels
  whose device is available, with a small retry budget (3) so a persistently
  failing device is not hammered; the budget resets when the device reappears,
  on resume, or on a user action.
- **Watchdog path.** A `DispatcherTimer` in `MixerViewModel` (`HealthInterval`,
  3 s) runs `RefreshDevices` (sync the live render-device list into `Sources` by
  id, disposing duplicate enumerations; update each channel's presence flag) and
  recovers if `!IsCapturing` **or** the source device id is no longer in the
  active list - covering the case where `RecordingStopped` is slow or never
  arrives. The whole tick is exception-guarded: a failing device can never turn
  the watchdog into a crash-dialog loop.
- **Resume recovery.** Two triggers, because `SystemEvents.PowerModeChanged` is
  documented-unreliable on Modern Standby: the `PowerModes.Resume` event, and a
  clock-jump fallback (a watchdog tick arriving > ~20 s late means the machine
  slept through timer time). `BeginResumeRecovery` then clears the unstartable-
  source list, hard-refreshes `Sources` (every cached instance replaced by a
  freshly enumerated one, `_selectedSource` re-pointed so the master strip talks
  to a live `AudioEndpointVolume`), restarts the engine and re-opens wanted
  channels. For ~10 further ticks (~30 s) the watchdog keeps resetting channel
  retry budgets so devices that come back staggered (TV over HDMI, Bluetooth)
  are picked up as they appear.

`TryRecover` (re-entrancy guarded) re-resolves a live source (the preferred one if
it is back, else the current default render device), calls `RestartEngine` to
rebuild capture and re-activate the channels whose intent is ON, then reports the
outcome in `EngineStatus` (`Source switched to 'X'.` when it had to fall back to a
different device, cleared on a clean same-device recovery) and persists the choice.

**Preferred vs. active source.** `_preferredSourceId` is the source the user
actually chose (persisted as `SourceDeviceId`); `_selectedSource` is whatever is
live, which may be a fallback when the preferred device is not ready yet - the
common case being Bluetooth earbuds still connecting just after a PC restart.
Only an explicit pick (the `SelectedSource` setter) changes the preference, and
`Save` writes `_preferredSourceId`, never a fallback, so a temporary fallback can
never overwrite the real preference. When the watchdog finds capture healthy but
running on a fallback, `TrySwitchToPreferred` switches back to the preferred device
as soon as it reappears (status `Source restored to 'X'.`). A device that refuses
to start is recorded in `_unstartableSources` and not retried until it disconnects
and reconnects, so the app never spins on, e.g., a device locked in exclusive mode.

## UI model (AudioHQ.App)

- `MixerViewModel` (root, `DataContext` of `MainWindow`):
  - `Sources` - all active render devices; picking one restarts the engine
    (`RestartEngine`: clear channels, start capture, rebuild one
    `ChannelViewModel` per OTHER device).
  - Master strip = `AudioEndpointVolume` of the SOURCE device (real Windows
    volume + mute of that device), NOT an in-app gain.
  - `LatencyPresets` (15/30/60/100 ms). Changing the preset re-opens every
    active channel so the new buffer size takes effect.
  - `EngineStatus` - human-readable capture state, shown as a dismissable
    notification toast in `MainWindow` (X button -> `DismissStatusCommand`).
    `EngineStatusIsError` carries the severity so the bubble colours itself:
    blue for informational notices (source switched/restored) and red for
    failures (e.g. source locked in exclusive mode `0x8889000A`, no device).
    Always set via the `SetStatus`/`ClearStatus` helpers so message and severity
    stay in sync (see "Source-loss recovery").
- `ChannelViewModel` (one per non-source device):
  - `IsActive` toggles mirroring (creates/removes the engine `OutputChannel`);
    activation failures map COM errors to short status strings:
    `0x8889000A` in use (exclusive) / `0x88890008` format not supported /
    `0x88890004` device unavailable.
  - `Gain` (0..2, shown as %) and `IsMuted` write through to the live channel.
- `GainToBrushConverter` - slider coloring (UI only).

### Per-app mixer (slide-out panel)

A second, independent surface next to the mirror strips: the left slide-out panel
is the Windows volume mixer, in app. It is **orthogonal to the mirror engine** -
it touches per-application Windows volumes, not the capture/fan-out pipeline.

- **Engine side (`AudioHQ.Core`).** `AppSessions.ForDefaultRender()` takes a fresh
  snapshot of the WASAPI audio sessions on the default render endpoint
  (`MMDevice.AudioSessionManager.Sessions`), skipping expired ones, and wraps each
  in an `AppSession`. `AppSession` resolves a friendly name (exe `FileDescription`
  -> process name -> declared `DisplayName`), the exe path (for the icon), a stable
  `Key` (the session instance identifier, or `pid:<n>`) plus an app-level
  `AppKey` based on executable path/icon/name, and exposes the session's own
  `Volume` (0..1) and `Muted` via `SimpleAudioVolume`. Every COM access is
  guarded - a session can expire mid-call - so reads fall back to last values and
  writes are no-ops on a dead session. The wrapper roots its source `MMDevice` so
  the session COM objects stay valid after the enumerator is gone. The snapshot is
  taken on demand, never polled.
- **UI side (`AudioHQ.App`).** `AppMixerViewModel` holds the rows
  (`AppSessionViewModel`) and an `IsExpanded`/`IsEmpty` state. `Refresh()` reconciles
  the live snapshot against the existing rows by `AppKey` (update in place / add /
  remove), grouping multiple sessions/processes from the same executable into one
  row so sliders do not rebuild or flicker. It refreshes on three triggers: the
  panel opening (`IsExpanded` setter),
  the window being activated (`MainWindow.Activated`, only while open), and the manual
  refresh button (`RefreshCommand`). `AppIcon` extracts a frozen `ImageSource` from the
  exe via `System.Drawing.Icon` + `Imaging.CreateBitmapSourceFromHIcon`; it returns null
  (neutral placeholder) for system sounds and unreadable/elevated apps. The panel width
  animates 0 <-> `AppMixerPanelWidth` (244) in `MainWindow.AnimateAppPanel`; the region binds to its own
  `AppMixerViewModel` (set in code-behind), separate from the window's `MixerViewModel`.
- **Row order (pin / drag).** `Apps` is the display order itself. Rows can be pinned
  (`PinCommand` -> `TogglePin`, which flips `AppSessionViewModel.IsPinned` and `Move`s the
  row to the pinned/unpinned boundary) and drag-reordered (`AppRow_DragStart` on the icon
  -> `MoveApp`), with reordering confined to a row's pin group so the pinned block stays on
  top. `Refresh` preserves this order - it updates rows in place by app key, drops ended
  rows, and restores newly-returned apps from the persisted app layout when present.
  `MixerSettings.AppMixerApps` stores app keys in display order with their pinned state,
  and keeps entries even while apps are absent so pin/order state comes back when a new
  session appears. `FluidMoveBehavior` (Microsoft.Xaml.Behaviors.Wpf) on the items
  `StackPanel` animates rows sliding to their new position on any reorder.

### Tray & startup (AudioHQ.App)

- `TrayController` owns a WinForms `NotifyIcon` (the only reason `UseWindowsForms`
  is on) loaded from `app.ico` embedded in the exe as a resource (so the portable
  single-file build needs no loose icon). It provides a Show/Exit menu,
  restore-on-double-click, and reads `MixerViewModel.MinimizeToTray` /
  `CloseToTray` live so the behaviour follows the Options toggles without a
  restart. `MainWindow.OnClosing` defers to it for close-to-tray.
- `StartupRegistration` toggles a per-user `HKCU\...\Run` entry for "Run with
  Windows"; the entry is re-synced to the current exe path on each launch.
- The three flags persist in `settings.json` (`MixerSettings`). The app/window
  icon comes from `<ApplicationIcon>app.ico</ApplicationIcon>`; the .ico is
  generated by `tools/make-icon.ps1`.

## Error handling & logging

- Philosophy: device errors are EXPECTED states, not crashes. They surface as
  per-strip `Status` / global `EngineStatus` strings; the app keeps running.
- `App.xaml.cs` hooks `DispatcherUnhandledException` (logs + message box,
  marks handled).
- `AudioHQ.Core.Log` appends to `audiohq.log` next to the exe; it swallows
  its own failures. Write a log line at every device/engine decision point
  (start, init mode, fallback, resync, failure).

## Known limitations / upgrade candidates

- Mixer state (active channels, gains, latency) is NOT persisted across runs.
- Device hot-plug is handled by a 3 s `DispatcherTimer` poll (`RefreshDevices` /
  source-loss recovery), not an `IMMNotificationClient` subscription. The poll is
  simple and robust but reacts within one interval rather than instantly; moving
  to the event-driven notification callback is a possible future refinement.
- Master strip moves the source device's real Windows volume; if the source
  is also a physical output the user hears, "master" and "that device's
  slider" are inherently the same control. A separate in-app pre-mirror gain
  is a possible future feature (user request pending - see chat history).
- `LoopbackMirror` (CLI) duplicates pipeline logic from `OutputChannel`;
  fold the CLI onto `MirrorEngine` if the duplication starts to drift.
- net7.0 is past Microsoft support EOL; migrating to net8.0 LTS is a cheap
  future chore (TFM bump in 3 csproj files; NAudio 2.3 is compatible).
