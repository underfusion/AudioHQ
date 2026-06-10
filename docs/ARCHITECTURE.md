# AudioHQ - Technical Architecture

> Keep this file truthful to the code. Update it in the same commit as any
> behavior change it describes (rule: CLAUDE.md "Documentation").
> Last updated: 2026-06-10 (v0.1.0).

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
   │  backlog check: BufferedDuration > latency+25ms -> ClearBuffer (resync)
   ▼
BufferedWaveProvider (2 s capacity, DiscardOnBufferOverflow)
   │
   ▼
[optional] WdlResamplingSampleProvider     capture rate != device mix rate
   │
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
- **Backlog resync.** If a device's queue exceeds `latency + 25 ms`, the
  buffer is cleared instead of letting audible delay accumulate (clock drift,
  device hiccups). Logged each time it happens.
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
  gain, mute, source/latency change). No cross-thread UI marshaling exists
  yet because the engine raises no events back to the UI.

## UI model (AudioHQ.App)

- `MixerViewModel` (root, `DataContext` of `MainWindow`):
  - `Sources` - all active render devices; picking one restarts the engine
    (`RestartEngine`: clear channels, start capture, rebuild one
    `ChannelViewModel` per OTHER device).
  - Master strip = `AudioEndpointVolume` of the SOURCE device (real Windows
    volume + mute of that device), NOT an in-app gain.
  - `LatencyPresets` (15/30/60/100 ms). Changing the preset re-opens every
    active channel so the new buffer size takes effect.
  - `EngineStatus` - human-readable capture failure (e.g. source locked in
    exclusive mode, `0x8889000A`).
- `ChannelViewModel` (one per non-source device):
  - `IsActive` toggles mirroring (creates/removes the engine `OutputChannel`);
    activation failures map COM errors to short status strings:
    `0x8889000A` in use (exclusive) / `0x88890008` format not supported /
    `0x88890004` device unavailable.
  - `Gain` (0..2, shown as %) and `IsMuted` write through to the live channel.
- `GainToBrushConverter` - slider coloring (UI only).

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
- The channel list is rebuilt on source change; device hot-plug while running
  is not handled (no `IMMNotificationClient` subscription yet).
- Master strip moves the source device's real Windows volume; if the source
  is also a physical output the user hears, "master" and "that device's
  slider" are inherently the same control. A separate in-app pre-mirror gain
  is a possible future feature (user request pending - see chat history).
- `LoopbackMirror` (CLI) duplicates pipeline logic from `OutputChannel`;
  fold the CLI onto `MirrorEngine` if the duplication starts to drift.
- net7.0 is past Microsoft support EOL; migrating to net8.0 LTS is a cheap
  future chore (TFM bump in 3 csproj files; NAudio 2.3 is compatible).
