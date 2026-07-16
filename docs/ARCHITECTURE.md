# AudioHQ - Technical Architecture

> Keep this file truthful to the code. Update it in the same commit as any
> behavior change it describes (repository documentation convention).
> Last updated: 2026-07-09 (v0.8.3).

## Solution layout

Four projects, strict dependency direction (UI never leaks into the engine):

```
AudioHQ.App (WPF, net7.0-windows) --.
                                     |--> AudioHQ.Core (net7.0, NAudio 2.3)
AudioHQ.Cli (console, net7.0)     --'
AudioHQ.Tests (xUnit, net7.0-windows) --> AudioHQ.Core + AudioHQ.App
```

- **AudioHQ.Core** - audio engine. No WPF/WinForms references, ever.
- **AudioHQ.App** - WPF GUI, plain MVVM (hand-rolled `ViewModelBase` with
  `INotifyPropertyChanged`, no MVVM framework).
- **AudioHQ.Cli** - minimal console front end used to test `MirrorEngine`
  without the GUI.
- **AudioHQ.Tests** - focused xUnit safety-net tests for hardware-free logic:
  EQ settings/model behavior and settings serialization. It targets
  `net7.0-windows` because it references app view-model types from the WPF
  project.

Version is centralized in `Directory.Build.props` (every assembly inherits
it); `AudioHQ.Core.AppVersion` exposes it to both front ends. SDK analyzers are
enabled through the same file with project-local suppressions for WPF lifetime
and test naming/exception patterns.

## Signal flow (MirrorEngine - the GUI path)

```
source MMDevice (render endpoint)
   |  WASAPI loopback capture (WasapiLoopbackCapture, ~10 ms chunks)
   v
MirrorEngine.OnDataAvailable          [capture thread]
   |  lock-free: iterates a published snapshot of the outputs
   v  per output:
OutputChannel.Write
   |  safety-net backlog check: BufferedDuration > latency+25ms -> ClearBuffer
   v
BufferedWaveProvider (2 s capacity, DiscardOnBufferOverflow)
   |
   v
AdaptiveResampler                          capture rate -> device mix rate,
   |                                       ratio nudged to hold backlog at target
   v
EqualizerProvider                          per-channel graphic EQ (3/6 peaking
   |                                       biquads + optional low-pass cascade
   |                                       per audio channel); off = bypass
   v
VolumeSampleProvider                       gain 0..2, mute = volume 0
   |
   v
WasapiOut (shared mode, event-sync; push-mode fallback)  -> physical device
```

Key decisions:

- **Fan-out at the byte level.** One capture feeds N independent per-device
  pipelines; each `OutputChannel` owns its buffer, resampler, EQ, gain and `WasapiOut`.
  A slow/failed device cannot stall the others (worst case it resyncs). The
  capture callback backs this up with a per-output `try/catch`: a channel that
  throws is logged once (`OutputChannel.NoteWriteFailure`) and skipped, so it
  cannot unwind the callback and cut audio to the rest.
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
- All UI-thread marshaling goes through one helper, `UiDispatcher.Post` (source
  lost, playback stopped, power-mode resume). It always uses `BeginInvoke`, never
  `Invoke`: these callbacks arrive on the audio threads, and a synchronous hop
  would block capture/render until the UI thread is free. With no `Application`
  (unit tests, shutdown) it runs the action inline.

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
  Ownership transfers on SUCCESS only: if the `OutputChannel` constructor throws
  it disposes just the audio client it created and leaves the device to the
  caller, which is what `ChannelActivationService.CleanUpFailedActivation` does.
  Disposing it on both sides would over-release the COM object.
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
  id, disposing duplicate enumerations; update each channel's presence flag). A
  channel also persists the endpoint's last known Windows friendly name separately
  from its editable label. If its id disappears and exactly one unclaimed active
  endpoint has that name, the channel adopts the replacement id and keeps all mixer
  state; ambiguous matches stay offline rather than routing to the wrong device.
  The watchdog then recovers if `!IsCapturing` **or** the source device id is no longer in the
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

- `App.xaml` merges resource dictionaries in dependency order:
  `Resources/Tokens.xaml` (converters, spacing and colours),
  `Resources/StripStyles.xaml` (shared strip/dialog/fader/scrollbar styles),
  then `Resources/AppMixerStyles.xaml` (app-mixer-specific controls).
  Keep resource keys stable because `MainWindow.xaml`, `EqWindow.xaml` and
  `OptionsWindow.xaml` reference them directly.
- `MainWindow.xaml` keeps the shell layout inline, but repeated strip markup is
  named: `AppMixerRowTemplate` renders app rows, `ChannelStripTemplate` renders
  output strips, and `MasterStripControl` owns the source master strip. The
  master control also owns the 100% unity-line positioning because that logic
  depends on named elements inside the strip.
- `MixerViewModel` (root, `DataContext` of `MainWindow`):
  - `Sources` - all active render devices; picking one delegates to
    `MixerSourceRecoveryViewModel`, which restarts capture and updates channel
    source flags without changing the public binding surface.
  - `LatencyPresets` (15/30/60/100 ms). Changing the preset re-opens every
    active channel so the new buffer size takes effect.
  - `Status` (`MixerStatusViewModel`) - human-readable capture state, shown as a
    dismissable notification toast in `MainWindow` (X button ->
    `DismissStatusCommand`). `Status.IsError` carries the severity so the bubble colours itself:
    blue for informational notices (source switched/restored) and red for
    failures (e.g. source locked in exclusive mode `0x8889000A`, no device).
    Always set via the `SetStatus`/`ClearStatus` helpers so message and severity
    stay in sync (see "Source-loss recovery").
  - Save projection is centralized in `MixerSettingsProjection`: `MixerViewModel`
    still decides when to save, but the mapping from live UI state to
    `MixerSettings` is a small tested helper.
  - **Autosave.** `MarkDirty` (gain, EQ, rename, active) flags the edit and re-arms
    a 2 s `DispatcherTimer`; the timer fires once after the last change, so a fader
    drag writes once on release instead of per change notification. `FlushPendingSave`
    also runs on window `Deactivated`, on `SystemEvents.SessionEnding` and from
    `Dispose`. This matters because close-to-tray only hides the window, so `Dispose`
    never runs while the app sits in the tray - edits used to be lost on a forced
    shutdown. `MixerSettings.Save` is atomic (write `settings.json.tmp`, flush to
    disk, then `File.Replace`/`Move`), so a kill mid-write cannot truncate the file;
    the previous settings survive. Covered by `MixerSettingsAtomicSaveTests`.
  - Tray/startup options are owned by `TrayOptions`
    (`MixerTrayOptionsViewModel`), which updates `MixerSettings`, saves changes,
    and synchronizes the Run-with-Windows registry entry.
- `MixerChannelCollectionViewModel` owns the curated output-channel list:
  building rows from persisted definitions, first-run seeding from available
  devices, add/remove/reorder commands, and the focused channel used by tray
  middle-click control. `MixerViewModel` still exposes the same `Channels`,
  `RemoveChannelCommand`, `FocusChannelCommand`, `FocusedChannel`,
  `AddChannel` and `MoveChannel` surface for existing XAML/code-behind bindings.
- `MixerMasterViewModel` owns the master strip rename state plus
  `AudioEndpointVolume` reads/writes for the selected source device. Master
  volume/mute are the real Windows volume and mute of the SOURCE device, NOT an
  in-app gain. Endpoint access is exception-guarded because cached devices can
  die between watchdog ticks.
- `MixerSourceRecoveryViewModel` owns source selection, preferred-source fallback,
  watchdog device refresh, unstartable-source retry suppression, source-lost
  handling and sleep/resume recovery. It keeps `_preferredSourceId` distinct from
  the live selected source so temporary fallbacks do not overwrite the user's
  saved source choice.
- `ChannelViewModel` (one per non-source device):
  - `IsActive` toggles mirroring. Live output creation is delegated to
    `ChannelActivationService`, which resolves a fresh `MMDevice`, calls
    `MirrorEngine.AddOutput`, applies gain/mute/EQ, wires playback-stop events,
    cleans up failed activation attempts, and maps COM errors to short status
    strings: `0x8889000A` in use (exclusive), `0x88890008` format not supported,
    `0x88890004` device unavailable.
  - `Gain` (0..2, shown as %) and `IsMuted` write through to the live channel. Both
    are persisted (`ChannelDefinition.Gain` / `.Muted`), so a channel muted at exit
    comes back muted and activation re-applies it. `Muted` is absent from settings
    written before it was persisted and defaults to unmuted.
  - `ChannelRetryBudget` limits watchdog auto-reactivation attempts for a
    persistently failing output. Resume recovery, fresh device appearance and
    explicit user toggles reset the budget; forced restart attempts bypass it.
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
  writes are no-ops on a dead session. The wrapper roots only its own
  `SimpleAudioVolume`: that COM reference is independent of the device and the
  enumerator, so `ForDefaultRender` disposes the `MMDevice` as soon as the snapshot
  is built and the rows keep reading AND writing per-app volume/mute afterwards.
  This was measured, not assumed - without that dispose the 2 s refresh leaked one
  handle per call (~1800/hour with the panel open) that GC never reclaimed. The
  snapshot API itself is synchronous and stateless; the UI decides when to call it.
- **UI side (`AudioHQ.App`).** `AppMixerViewModel` holds the rows
  (`AppSessionViewModel`) and an `IsExpanded`/`IsEmpty` state. `Refresh()` reconciles
  the live snapshot against the existing rows by `AppKey` (update in place / add /
  remove), grouping multiple sessions/processes from the same executable into one
  row so sliders do not rebuild or flicker. Opening the panel runs an immediate
  refresh and starts a 2 s `DispatcherTimer`; closing the panel stops the timer.
  `AppIcon` extracts a frozen `ImageSource` from the
  exe via `System.Drawing.Icon` + `Imaging.CreateBitmapSourceFromHIcon`; it returns null
  (neutral placeholder) for system sounds and unreadable/elevated apps. `AppPanelAnimator`
  animates the panel width 0 <-> `AppMixerPanelWidth` (244) and the matching open/closed
  margin tokens; the region binds to its own `AppMixerViewModel` (set in code-behind),
  separate from the window's `MixerViewModel`.
- **Row order (pin / drag).** `Apps` is the display order itself. Rows can be pinned
  (`PinCommand` -> `TogglePin`, which flips `AppSessionViewModel.IsPinned` and `Move`s the
  row to the pinned/unpinned boundary) and drag-reordered through
  `AppRowDragController`, which owns the ghost adorner, drop highlight and `MoveApp`
  call. Reordering is confined to a row's pin group so the pinned block stays on top.
  The pure ordering rules live in `AppMixerLayout`, which lets tests cover pinning,
  drag moves, saved-order replay and absent-row persistence without live WASAPI sessions.
  `Refresh` preserves this order - it updates rows in place by app key, drops ended
  rows, and restores newly-returned apps from the persisted app layout when present.
  `MixerSettings.AppMixerApps` stores app keys in display order with their pinned state,
  and keeps entries even while apps are absent so pin/order state comes back when a new
  session appears. `FluidMoveBehavior` (Microsoft.Xaml.Behaviors.Wpf) on the items
  `StackPanel` animates rows sliding to their new position on any reorder.
- **Docking.** The mixer header can move the existing `AppPanel` between the main
  window and a borderless `AppMixerWindow`, so both modes share the same view model,
  rows, drag behavior and scroll state. The detached host follows the main window's
  left edge with the same 8 px child-window gap used by EQ placement. Its height follows
  the visible app rows up to the available work-area height. It opens fully expanded by
  default for up to 10 apps (and no farther than the work area); a centered bottom handle
  can shorten it.
  It derives the limit from the live app count and the actual rendered row height, avoiding WPF's
  constrained scroll-viewport measurement. A user drag can shorten it. The detached
  host uses the same native draggable caption as the other app windows;
  its X button hides rather than destroys it. Dock and attached expansion state persist
  separately in `MixerSettings`, allowing startup to restore attached-open,
  attached-closed, or detached exactly. The main window's left rail hides or shows the
  detached host and keeps the mixer refresh state synchronized with its visibility.
  Attach restores the embedded panel
  fully expanded. Its matched dock-action glyphs use a dim left-pointing Detach icon
  and a bright right-pointing Attach icon.
- **Main-window placement.** First launch uses WPF's centered-screen placement. Later
  launches restore the last normal `Left`/`Top` stored in `MixerSettings`, provided the
  point still intersects the current virtual desktop; invalid coordinates fall back to center.
  `WindowPlacement.FollowOwner` keeps modeless EQ and Options windows at their current
  offsets when the main window moves; manually moving a child updates that offset.
- **EQ preset actions.** `EqWindow` retains the last selected non-Default preset when
  its curve is edited, displaying `Name (not saved)` over the combo. Combo selection loads
  immediately. Reset enables only for unsaved changes and reloads the active preset,
  including Default. With an empty new-name field the green save action overwrites a
  modified non-Default preset; typing a name switches it to save a named preset. Default
  remains read-only, and the destructive Delete action is styled red.

### Tray & startup (AudioHQ.App)

- `TrayController` owns a WinForms `NotifyIcon` (the only reason `UseWindowsForms`
  is on) loaded from `app.ico` embedded in the exe as a resource (so the portable
  single-file build needs no loose icon). It provides a Show/Exit menu,
  restore-on-double-click, and reads `MixerViewModel.TrayOptions.MinimizeToTray` /
  `TrayOptions.CloseToTray` live so the behaviour follows the Options toggles without a
  restart. `MainWindow.OnClosing` defers to it for close-to-tray. Options and EQ are
  owned modeless windows. Hiding to tray snapshots and hides the entire visible owned
  window set; restore shows that exact set and returns its previous active window.
  `Application.ShutdownMode=OnMainWindowClose` guarantees a real main-window close
  terminates the app and all owned windows regardless of which dialogs are open.
- `MainWindowTraySync` subscribes to mixer/channel property changes and keeps the
  tray tooltip, focused-channel tray overlay and WPF taskbar icon overlay in sync.
  `WindowIconFactory` creates the base and focused taskbar `BitmapSource` variants.
- `RenameTextBoxController` centralizes inline rename Enter/Escape/lost-focus behavior.
  `ChannelDragDropController` handles channel-strip drag/drop reorder events.
- `StartupRegistration` toggles a per-user `HKCU\...\Run` entry for "Run with
  Windows"; the entry is re-synced to the current exe path on each launch.
- `App.OnStartup` creates the main window explicitly instead of using `StartupUri`,
  allowing the persisted launch-minimized option to keep it hidden from the first
  frame while the tray icon and audio engine still start normally.
- The four flags persist in `settings.json` (`MixerSettings`). The app/window
  icon comes from `<ApplicationIcon>app.ico</ApplicationIcon>`; the .ico is
  generated by `tools/make-icon.ps1`.

## Styling rules (AudioHQ.App)

- **One source of truth.** `Resources/Theme/` owns every visual value: `Colors.xaml`
  is the only place a hex literal belongs, `Semantic.xaml` names what a colour is
  FOR (`Brush.Surface`, `Brush.AccentPositive`), and Typography/Spacing/Motion own
  sizes, the 6px spacing rhythm plus radii, and durations/easing.
- **Views and styles name roles, not values.** No view sets a raw `FontSize`,
  `CornerRadius`, colour or margin; it references a token. `Resources/Controls/`
  holds one file per control family, keyed styles only.
- **Margins need a Thickness.** The `Spacing*` doubles cannot bind to `Margin`, so
  use the `Inset.*` (uniform) and `Gap.*` (directional) thicknesses. Sizes that are
  genuinely one-off geometry stay literal rather than being forced onto the scale.
- **C# reads the same theme.** `ThemeResources.Brush/Color/DrawingColor` is the only
  way code gets a theme value, and it throws on a missing key. Never re-declare a
  colour in C# with a fallback - that is how the fader ramp, the knob and the tray
  dot each ended up with their own drifting green.
- **Coupled geometry gets a shared token.** Where two places must agree (the EQ
  preset overlay sits on top of the combo's selection text), both read one token
  (`ComboContentInset`) instead of repeating the number.

## Error handling & logging

- Philosophy: device errors are EXPECTED states, not crashes. They surface as
  per-strip `Status` / global `EngineStatus` strings; the app keeps running.
- `App.xaml.cs` hooks `DispatcherUnhandledException` (logs + message box,
  marks handled).
- `AudioHQ.Core.Log` appends to `audiohq.log` next to the exe; it swallows
  its own failures. Write a log line at every device/engine decision point
  (start, init mode, fallback, resync, failure).
- Teardown never throws. `MirrorEngine.Stop` guards each step (stop capture,
  dispose capture, dispose outputs, dispose source) individually and logs what
  it swallowed, so a driver that throws on an already-dead device cannot leave
  the engine holding a stale capture. `Start` calls `Stop` first, so this also
  makes `Stop` safe to re-enter - the source-loss watchdog
  (`MixerSourceRecoveryViewModel.TryRecover`) depends on it.

## Known limitations / upgrade candidates

- Automated coverage is intentionally small but present: `AudioHQ.Tests` covers
  hardware-free EQ, settings and app-mixer ordering behavior. Device enumeration, live WASAPI audio
  flows, tray behavior and WPF layout still need manual verification until more
  seams are extracted.
- Device hot-plug is handled by a 3 s `DispatcherTimer` poll (`RefreshDevices` /
  source-loss recovery), not an `IMMNotificationClient` subscription. The poll is
  simple and robust but reacts within one interval rather than instantly; moving
  to the event-driven notification callback is a possible future refinement.
- Master strip moves the source device's real Windows volume; if the source
  is also a physical output the user hears, "master" and "that device's
  slider" are inherently the same control. A separate in-app pre-mirror gain
  is a possible future feature (user request pending - see chat history).
- `LoopbackMirror` is now a legacy milestone-1 reference path. The CLI tester
  uses `MirrorEngine`, so console smoke tests exercise the same fan-out path as
  the WPF app.
- net7.0 is past Microsoft support EOL, but this workspace currently has only
  .NET SDK 7.0 installed. Defer the `net8.0` migration until a .NET 8 SDK is
  available, then bump the four TFMs (`App`, `Core`, `Cli`, `Tests`) together
  and re-run the full suite.
