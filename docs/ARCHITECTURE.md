# AudioHQ - Technical Architecture

> Keep this file truthful to the code. Update it in the same commit as any
> behavior change it describes (rule: CLAUDE.md "Documentation").
> Last updated: 2026-06-11 (v0.2.8).

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
   │  lock(_lock) - iterate outputs
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
   │                                       biquads per audio channel); off = bypass
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
  3-band (100 / 1k / 8k Hz) or 6-band (80 / 200 / 500 / 1.2k / 3k / 8k Hz),
  +/-12 dB each. Each band also has a per-band Q (bell width, clamped to
  `EqBands.QMin..QMax`); `EqSettings.QValues` carries it, falling back to the
  band-count default (`Q3`/`Q6`) when unset. Disabled by default (pure
  pass-through). The UI reconfigures it live; `Configure` rebuilds the filter
  bank and publishes it atomically under a lock so a gain change cannot tear a
  filter mid-block on the audio thread. EQ state (enable, band count, gains, Q)
  is persisted per channel in `settings.json`. The editor draws the response
  curve as a sum of per-band bells whose width follows Q, so it tracks both the
  gain faders and the Q knobs.
- **Push-mode fallback.** Some drivers (notably NVIDIA HDMI) reject
  event-driven shared mode; `OutputChannel` retries with `useEventSync:false`.
- **`LoopbackMirror`** is the milestone-1 single-target version of the same
  pipeline, kept for the CLI tester. If engine behavior changes, prefer
  changing `MirrorEngine`; `LoopbackMirror` may lag behind feature-wise.

## Threading model

- WASAPI capture delivers buffers on NAudio's capture thread.
  `MirrorEngine.OnDataAvailable` runs there and only does: lock, iterate,
  `BufferedWaveProvider.AddSamples`. Keep this path allocation-light and fast.
- `_lock` in `MirrorEngine` guards the outputs list (Add/Remove vs. iteration).
- Each `WasapiOut` runs its own render thread reading from the buffered
  provider chain.
- ViewModels touch the engine only from the UI thread (activate/deactivate,
  gain, mute, source/latency change).
- `MirrorEngine.SourceLost` is the one event the engine raises back to the UI.
  NAudio fires `WasapiCapture.RecordingStopped` on the sync-context captured at
  `StartRecording` (the UI thread here), so it normally arrives on the UI thread,
  but `MixerViewModel.OnEngineSourceLost` marshals through `Dispatcher` defensively
  before touching view state.

## Source-loss recovery (watchdog)

The capture source can vanish mid-session (USB dongle unplugged, device disabled).
Two mechanisms keep the app alive and self-healing:

- **Event path.** `MirrorEngine` subscribes to `RecordingStopped`. An unsolicited
  stop (the handler is detached before an intentional `Stop`) means the source
  endpoint was invalidated: `IsCapturing` goes false and `SourceLost` fires.
  `MixerViewModel.HandleSourceLost` shows a status and calls `TryRecover`.
- **Watchdog path.** A `DispatcherTimer` in `MixerViewModel` (`HealthInterval`,
  3 s) runs `RefreshDevices` (sync the live render-device list into `Sources`,
  add/remove by id, re-point each channel across an offline/online transition) and
  recovers if `!IsCapturing` **or** the source device id is no longer in the active
  list - covering the case where `RecordingStopped` is slow or never arrives.

`TryRecover` (re-entrancy guarded) re-resolves a live source (the preferred one if
it is back, else the current default render device), calls `RestartEngine` to
rebuild capture and re-activate the channels that were ON, then reports the outcome
in `EngineStatus` (`Source switched to 'X'.` when it had to fall back to a
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
  - `EngineStatus` - human-readable capture state: failures (e.g. source locked
    in exclusive mode, `0x8889000A`) and source-loss/recovery notices (see
    "Source-loss recovery").
- `ChannelViewModel` (one per non-source device):
  - `IsActive` toggles mirroring (creates/removes the engine `OutputChannel`);
    activation failures map COM errors to short status strings:
    `0x8889000A` in use (exclusive) / `0x88890008` format not supported /
    `0x88890004` device unavailable.
  - `Gain` (0..2, shown as %) and `IsMuted` write through to the live channel.
- `GainToBrushConverter` - slider coloring (UI only).

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
