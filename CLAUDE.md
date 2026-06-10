# CLAUDE.md - AudioHQ

AudioHQ is a Windows audio mirror/mixer. It captures everything playing on one
output device (WASAPI loopback) and mirrors the stream to any number of other
output devices, each with its own gain/mute strip. One source, many outputs,
per-device volume - like a small hardware monitor controller in software.

**Stack:** .NET 7 / C#, WPF (MVVM, no framework), NAudio 2.3. Windows-only.

## Project map

```
AudioHQ.sln                  solution (3 projects)
Directory.Build.props        CANONICAL VERSION lives here (see Versioning)
start.bat                    kill running app -> build -> launch (daily driver)
src/
  AudioHQ.Core/              audio engine, no UI dependencies
    MirrorEngine.cs          capture + fan-out to N OutputChannels (gain/mute each)
    LoopbackMirror.cs        milestone-1 single-target mirror (used by CLI)
    AudioDevices.cs          WASAPI render-endpoint enumeration
    AppVersion.cs            exposes Directory.Build.props version to UIs
    Log.cs                   file logger -> audiohq.log next to the exe
  AudioHQ.App/               WPF GUI
    MainWindow.xaml(.cs)     window shell, title shows version
    ViewModels/
      MixerViewModel.cs      source pick, latency presets, master strip, channel list
      ChannelViewModel.cs    one output strip (activate/gain/mute/status)
  AudioHQ.Cli/               console tester for the core engine
docs/
  ARCHITECTURE.md            technical documentation (signal flow, threading, errors)
CHANGELOG.md                 one entry per version bump
```

## Rules

### Versioning (MANDATORY)

- Canonical version = `<Version>` in `Directory.Build.props`. Nowhere else.
- Bump the PATCH digit on EVERY edit batch you make (one bump per commit):
  `0.1.0 -> 0.1.1 -> ... -> 0.1.99`.
- MINOR bumps (`0.1 -> 0.2`) happen ONLY when the user explicitly says so.
  Same for `1.0`. Never decide these yourself.
- Every bump updates `CHANGELOG.md` (what changed, one short block) and
  re-checks `docs/ARCHITECTURE.md` - if the change touched anything described
  there, fix the description in the same commit.

### Documentation

- `docs/ARCHITECTURE.md` must stay truthful to the code. Update it with the
  commit that changes behavior, not later.
- `README.md` is the human entry point: what it is, how to build/run.
- New module or significant class -> add it to the Project map above.

### File structure

- Keep files small and single-purpose: one class per file, soft cap ~300
  lines. If a file approaches the cap, split it (partial classes are a smell;
  prefer extracting real types) BEFORE it balloons.
- UI logic lives in ViewModels; `AudioHQ.Core` must never reference WPF.
- New audio features go into `AudioHQ.Core` first, UI binds to them.

### Git

- Daily work on `dev`; `main` is for releases (user-gated).
- Commit per logical change with a conventional prefix (`feat:`, `fix:`,
  `docs:`, `chore:`, `refactor:`); mention the version in the body or subject
  when bumping. Push `dev` to `origin` after each session.
- Remote: https://github.com/underfusion/AudioHQ

### Language & style

- Everything in English: code, comments, docs, commit messages (responses to
  the user may be Polish only when explicitly requested per message).
- Use plain hyphens (-), never em dashes, in all docs and text.
- Logging: `Log.Write` at every device/engine decision point; logging must
  never throw.

## Build & run

```
start.bat                        # kill + build + launch the GUI
dotnet build AudioHQ.sln         # build everything
dotnet run --project src/AudioHQ.Cli    # console mirror tester
```

Runtime log: `audiohq.log` next to the built exe
(`src/AudioHQ.App/bin/Debug/net7.0-windows/`).

## Domain notes (read before touching audio code)

- "Master" strip = Windows volume of the SOURCE device
  (`AudioEndpointVolume`), not an in-app gain. Channel strips are in-app
  gains applied to the mirrored copies only.
- Mirroring a source that another app holds in EXCLUSIVE mode fails with
  `0x8889000A` (AUDCLNT_E_DEVICE_IN_USE) - this is surfaced in the UI, not a bug.
- Some drivers (NVIDIA HDMI) reject event-sync shared mode; `OutputChannel`
  falls back to push mode automatically.
- Output backlog above `latency + 25ms` is dropped (resync) to stop delay creep.
- Latency preset changes re-open all active channels.
