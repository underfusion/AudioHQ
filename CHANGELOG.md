# Changelog

All notable changes to AudioHQ. One entry per version bump
(see CLAUDE.md "Versioning" - patch bump on every edit batch).

## 0.2.6 - 2026-06-11

Portable release packaging.

### Changed
- The app executable is now `AudioHQ.exe` (process name `AudioHQ`) instead of
  `AudioHQ.App.exe`, via `<AssemblyName>`. `start.bat` and the tool scripts were
  updated to match.
- `app.ico` is now embedded in the exe as a resource; `TrayController` loads the
  tray icon from that resource stream instead of a loose file next to the exe.
  This makes the portable build a genuine single file (no icon to ship alongside).

### Added
- `tools/publish.ps1`: builds a self-contained, single-file, compressed `win-x64`
  portable exe, stages it into `release/AudioHQ-<version>-win-x64/` and zips it to
  `release/AudioHQ-<version>-win-x64-portable.zip`. Version is read from
  `Directory.Build.props`.
- README "Portable release" section documenting the build and the on-disk layout.
- `.gitignore`: ignore `release/`.

## 0.2.5 - 2026-06-11

Documentation.

### Added
- Rewrote `README.md` for GitHub: why/use-cases, full feature list (drift
  compensation, tray, startup, persistence), how-it-works diagram and a window
  screenshot (`docs/screenshot.png`).
- `tools/capture-screenshot.ps1`: captures the running window via `PrintWindow`
  (overlap-proof) for the README.

## 0.2.4 - 2026-06-11

### Changed
- Trimmed the backlog trough target from `latency + 10 ms` to `latency + 5 ms`
  to shave ~5 ms off total latency. Diagnostics showed the trough locking exactly
  on target with only tiny drift correction, so a smaller margin is viable;
  revert toward +10 ms if a jittery source crackles.

## 0.2.3 - 2026-06-11

Drift compensation now copes with bursty (wireless) sources.

### Changed
- `AdaptiveResampler` now steers the per-window MINIMUM backlog (the trough),
  recomputed ~5x/second, instead of a smoothed average. Diagnostics revealed a
  wireless source (PlayStation Link) delivering audio in ~60 ms bursts, so the
  backlog saw-tooths from ~10 ms to ~70 ms. Targeting the average dragged the
  trough below one render pull, starving the buffer and feeding silence (the
  crackle); targeting the trough keeps the low point safe whatever the burst
  size, while letting the peaks ride. Recomputing only a few times per second
  also removes the per-callback ratio jitter.
- `targetSeconds` (still `latency + 10 ms`) is now the trough target, i.e. the
  guaranteed minimum backlog, not an average.

## 0.2.2 - 2026-06-11

Resampler hardening + crackle diagnostics.

### Fixed
- `AdaptiveResampler.Read` now loops until it has filled the entire output buffer.
  A single `WdlResampler.ResampleOut` can return fewer frames than requested
  (fractional position / filter priming); the previous single-shot version left
  the tail of every WASAPI buffer as silence, a likely source of the crackle.

### Added
- Temporary throttled diagnostics (one line/second per output) logging the
  backlog range, applied correction, worst loop depth and starved reads, to pin
  down any residual crackle. To be removed once the cause is confirmed.

## 0.2.1 - 2026-06-11

Drift-compensation fix - no more crackle.

### Fixed
- The adaptive backlog target was too low (`(latency + 25) / 2`), which at the
  30 ms preset came out BELOW the latency itself, so the buffer underran between
  WASAPI pulls and silence was fed in (audible crackle). The target is now
  `latency + 10 ms`, one capture chunk of headroom above a full pull, so the
  buffer never starves while still staying under the hard-resync threshold.

## 0.2.0 - 2026-06-11

Drift compensation - the mirrored audio no longer slowly slides out of sync.

### Added
- `AudioHQ.Core/AdaptiveResampler`: a sample-rate converter that continuously
  nudges its ratio (by at most 0.5%, inaudible) to keep each output's backlog at
  a steady target. This corrects the unavoidable clock drift between the capture
  device and each output device, so the latency holds instead of creeping up.

### Changed
- `OutputChannel` now always runs through `AdaptiveResampler` (it folds in the
  capture-rate -> device-rate conversion that the optional resampler used to do).
  It targets a backlog of `(latency + 25 ms) / 2`.
- The hard backlog flush (`BufferedDuration > latency + 25 ms -> ClearBuffer`) is
  now only a safety net for stalls/glitches; in normal use it no longer fires,
  so the periodic "jump" you could hear on low-latency presets is gone.

## 0.1.9 - 2026-06-11

UI layout cleanup.

### Changed
- Removed MUTE button from all strips (master and channels).
- ON button now shows "OFF" when the channel is inactive.
- Slider centred properly within each card (24 px balance column added to
  the left of the fader, matching the scale-labels column on the right).
- Double-click to reset gain to 100% now works on the entire fader zone
  (below the EQ button), not just on the slider thumb itself.
- Options (gear) button moved to the top-right corner of the header row.
- Window height reduced slightly (ControlsZone 92 -> 60 px, two buttons).

## 0.1.8 - 2026-06-10

### Changed
- Options (settings) button now uses a minimalist 6-tooth cog (vector path)
  instead of the busier Segoe MDL2 gear glyph.

## 0.1.7 - 2026-06-10

Header spacing and tray tooltip.

### Changed
- Master label is top-aligned again (matching the channel titles) and the gap
  between every strip title and its ON/MUTE buttons is smaller (header zone
  58 -> 46 px).
- Tray icon hover text now lists which outputs are ON and which are OFF, updated
  live as channels toggle.

## 0.1.6 - 2026-06-10

Tray interaction.

### Changed
- A single left-click on the tray icon now toggles the window (show/hide to
  tray); was double-click to restore only.

## 0.1.5 - 2026-06-10

Fader interaction and master strip polish.

### Changed
- The percent readout and the 100% reference line are green up to unity; amber
  in the 100-125% boost zone, red above (was amber already at 100%).
- The 100% reference lines are green (were blue) and now sit behind the fader
  thumb; the master line dropped to line up with the thumb at 100%.
- Scale numerals (150/100/50) re-aligned to the fader thumb positions.
- Single-clicking a fader now jumps the thumb to the clicked point
  (`IsMoveToPointEnabled`); double-click still snaps to 100%.

### Added
- The master strip label is editable (double-click to rename, persisted as
  `MasterName`); it is bottom-aligned so a short name leaves no gap above the
  ON/MUTE buttons.

## 0.1.4 - 2026-06-10

App icon and system-tray integration.

### Added
- Minimalist AudioHQ icon (three EQ bars on a dark rounded tile) used as the
  exe/window icon and the tray icon. Source generator: `tools/make-icon.ps1`.
- System-tray icon (`TrayController`) with a Show/Exit menu; double-click the
  tray icon to restore the window.
- Options now has a "tray & startup" section: "Close to system tray",
  "Minimize to system tray" and "Run with Windows".
- "Run with Windows" (`StartupRegistration`) writes a per-user HKCU Run entry;
  it is re-synced to the current exe path on every launch.

## 0.1.3 - 2026-06-10

Fader and options polish.

### Changed
- Options button now uses the real Segoe MDL2 gear glyph (was a flower-like
  symbol).
- The 100% mark on every fader is a solid reference line instead of two small
  tick dashes (50%/150% stay as ticks).

### Added
- Double-click a fader to snap it back to 100% (unity).

## 0.1.2 - 2026-06-10

Strip controls and an Options dialog. Faders gained threshold ticks; source and
latency moved out of the main window so it sits narrower.

### Added
- Fader threshold ticks with numeric scale labels (master 0-100%, channels
  0-150%); the percent readout stays color-zoned (green -> amber -> red boost).
- Options window (gear button in the header) holding the source and latency
  pickers, bound to the same `MixerViewModel`.
- Master strip shows disabled ON/EQ placeholder buttons so every strip aligns.

### Changed
- Source and latency pickers removed from the top bar; with them gone the
  window auto-sizes much narrower to the strips.

### Note
- Master stays 0-100% (it is the source device's Windows volume, which Windows
  caps at 100%); a boost above 100% is not possible without routing the source
  through a virtual device.

## 0.1.1 - 2026-06-10

Curated channels: the mixer is no longer an auto-list of every device. It now
holds a user-owned, persisted set of named output strips.

### Added
- `MixerSettings` / `ChannelDefinition`: mixer state (source, latency, ordered
  named channels with gain) persisted to `settings.json` next to the exe;
  missing/corrupt file falls back to defaults (first run seeds from devices).
- `RelayCommand`: minimal `ICommand` for the hand-rolled MVVM.
- Channel editing in the UI: inline rename (double-click the name), add a
  channel (+ card -> pick an unused device), remove a channel (x), and
  drag-and-drop reorder via the channel grip.

### Changed
- Master strip set apart with a lighter shade and accent border; master and
  channel faders share fixed header/controls zones so all sliders start at the
  same height.
- `ChannelViewModel` is now a curated strip keyed by a persisted device id:
  survives source changes, can be offline (saved device unplugged) or equal to
  the source, and persists its name/gain.
- `MixerViewModel` loads/saves `MixerSettings` and exposes add/remove/move and
  available-device queries instead of rebuilding the list per source.

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
