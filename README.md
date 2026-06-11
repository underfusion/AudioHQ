# AudioHQ

A small Windows audio mirror and mixer. Pick one output device as the source,
and AudioHQ captures everything playing on it (WASAPI loopback) and mirrors that
stream to any number of other output devices - each with its own volume slider.
One source, many outputs, per-device volume. Like a small hardware monitor
controller, in software.

<p align="center">
  <img src="docs/screenshot.png" alt="AudioHQ window: a MASTER strip and an output strip with faders" width="460">
</p>

> **Stack:** .NET 7, C#, WPF (hand-rolled MVVM), NAudio 2.3. Windows only.

## Why

Windows lets you play to one output device at a time. AudioHQ removes that limit:

- Play music on your desk speakers and your headphones at the same time.
- Send the same audio to a second room or a second amp.
- Keep a Bluetooth headset and a wired DAC in sync, with independent volume each.

## Features

- **Mirror one source to many outputs.** Capture any active render device and fan
  it out to as many other devices as you like.
- **Per-output volume.** Every output is a strip with an ON/OFF mirror toggle and
  a gain fader (0-200%, unity at 100%, colour-zoned green/amber/red).
- **Master strip.** Controls the Windows volume of the *source* device directly.
- **Adaptive drift compensation.** Independent device clocks normally let the delay
  slowly creep until an audible re-sync jump. AudioHQ continuously nudges each
  output's resample ratio (by a fraction of a percent - inaudible) to hold the
  latency steady. It tracks the buffer's low point, so it copes even with bursty
  wireless sources that deliver audio in large chunks.
- **Latency presets.** Ultra 15 ms / Low 30 ms / Balanced 60 ms / Safe 100 ms.
- **Curated, persistent channels.** Add or remove outputs, rename them
  (double-click), and drag to reorder. Your set is saved to `settings.json`.
- **System tray.** Close or minimize to the tray, single-click the tray icon to
  show/hide the window, and hover it to see which outputs are ON and which are OFF.
- **Run with Windows.** Optional per-user startup entry.
- **Honest error states.** Exclusive-mode lock, unsupported format and unplugged
  devices each surface as a clear per-strip status instead of a crash.

## Requirements

- Windows 10 or 11
- To run: [.NET 7 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/7.0)
- To build: .NET 7 SDK

## Build and run

```
start.bat                              # kill running app, build, launch the GUI (daily driver)
dotnet build AudioHQ.sln               # build everything
dotnet run --project src/AudioHQ.Cli   # console tester: mirror to a single device
```

The runtime log is written to `audiohq.log` next to the executable
(`src/AudioHQ.App/bin/Debug/net7.0-windows/`).

## Portable release

```
powershell -ExecutionPolicy Bypass -File tools/publish.ps1
```

This produces a **self-contained, single-file** `win-x64` build (no .NET install
needed) and zips it:

```
release/
└─ AudioHQ-<version>-win-x64-portable.zip
   └─ AudioHQ-<version>-win-x64/
      └─ AudioHQ.exe          one file (~70 MB, runtime + icon embedded)
```

On first run the app creates its files **next to the exe** - it is fully portable
and keeps nothing in `%AppData%`:

```
AudioHQ-<version>-win-x64/
├─ AudioHQ.exe
├─ settings.json     source, channels, gains and options
└─ audiohq.log       runtime log
```

Unzip into a user-writable folder (Desktop, Documents, a USB drive), not
`C:\Program Files` - the app writes `settings.json` / `audiohq.log` beside itself.
The exe is unsigned, so the first launch shows a Windows SmartScreen prompt
("More info" -> "Run anyway").

## How it works

```
source device --(WASAPI loopback)--> capture --> fan-out
                                                    |--> output 1: buffer -> adaptive resampler -> gain -> WASAPI render
                                                    |--> output 2: ...
                                                    `--> output N: ...
```

One capture feeds N independent pipelines. Each output owns its buffer, drift
compensator, gain and render client, so a slow or failed device can never stall
the others. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full signal
flow, threading model and error handling.

## Project layout

```
src/AudioHQ.Core   audio engine (NAudio): capture, fan-out, per-channel gain, drift compensation
src/AudioHQ.App    WPF GUI (MVVM): mixer window, strips, tray, options
src/AudioHQ.Cli    console tester for the engine
docs/              technical documentation and the screenshot
tools/             helper scripts (icon generator, screenshot capture)
```

## Documentation

- **Changelog:** [CHANGELOG.md](CHANGELOG.md) - one entry per version.
- **Architecture:** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) - technical deep dive.

The canonical version lives in `Directory.Build.props` and is shown in the window title.

## License

[MIT](LICENSE) - free to use, modify, distribute and sell, including
commercially. The only condition is keeping the copyright notice. No warranty.
