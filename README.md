# AudioHQ

A small Windows audio mirror/mixer. Pick a source output device and AudioHQ
captures everything playing on it (WASAPI loopback) and mirrors the stream to
any number of other output devices - each with its own volume slider and mute.

Use cases: play music on speakers and headphones at once, feed a second room,
keep a Bluetooth headset and the desk DAC in sync - with independent volume
per device.

**Version:** see `Directory.Build.props` (shown in the window title).
**Changelog:** [CHANGELOG.md](CHANGELOG.md).
**Technical docs:** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
**Project rules (versioning, structure, git):** [CLAUDE.md](CLAUDE.md).

## Features

- Source picker: any active render device (defaults to the Windows default output)
- Master strip: controls the Windows volume of the source device
- One strip per other output device: activate (mirror on/off), gain (0-200%), mute
- Latency presets: Ultra 15 ms / Low 30 ms / Balanced 60 ms / Safe 100 ms
- Automatic resync when an output falls behind (no creeping delay)
- Clear per-device error states (exclusive-mode lock, unsupported format, unplugged)

## Requirements

- Windows 10/11
- .NET 7 SDK (build) / .NET 7 Desktop Runtime (run)

## Build & run

```
start.bat                              # build + launch the GUI (recommended)
dotnet build AudioHQ.sln               # build everything
dotnet run --project src/AudioHQ.Cli   # console tester: mirror to one device
```

Runtime log: `audiohq.log` next to the executable.

## Project layout

```
src/AudioHQ.Core   audio engine (NAudio): capture, fan-out, per-channel gain
src/AudioHQ.App    WPF GUI (MVVM)
src/AudioHQ.Cli    console tester for the engine
docs/              technical documentation
```

## License

Private - all rights reserved (for now).
