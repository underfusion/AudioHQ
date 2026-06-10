# Changelog

All notable changes to AudioHQ. One entry per version bump
(see CLAUDE.md "Versioning" - patch bump on every edit batch).

## 0.1.0 - 2026-06-10

Baseline release: first versioned state of the project.

### Added
- `AudioHQ.Core`: `MirrorEngine` (WASAPI loopback capture fanned out to N
  `OutputChannel`s, per-channel gain/mute, backlog resync, push-mode fallback
  for drivers that reject event-sync), `LoopbackMirror` (single-target
  milestone-1 mirror), `AudioDevices`, `Log`.
- `AudioHQ.App`: WPF mixer GUI - source picker, latency presets (15/30/60/100 ms),
  master strip bound to the source device's Windows volume, one strip per
  output device (activate/gain/mute/error status).
- `AudioHQ.Cli`: console tester (mirror the default output to a chosen device).
- Central version in `Directory.Build.props` (`AppVersion` helper surfaces it
  in the window title and CLI banner).
- Documentation package: README, CLAUDE.md (project rules: versioning,
  file-structure, git, language), docs/ARCHITECTURE.md, this changelog.
- `.gitignore` extended with .NET build artifacts (`bin/`, `obj/`, `.vs/`)
  and local agent files.
