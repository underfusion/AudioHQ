# AudioHQ Refactor Plan

Technical audit date: 2026-07-09  
Current baseline: v0.8.3 refactor complete  
Overall refactor progress: 100%

This document is the working checkpoint plan for making AudioHQ easier to extend,
safer to change, and better documented. Keep the percentages current whenever a
checkpoint is started or completed.

## Audit Summary

AudioHQ has a clear project split (`Core`, `App`, `Cli`) and the audio pipeline
is documented well. The original audit found oversized root files and no test
project; the refactor has reduced the main hotspots and added executable safety
nets:

- `MixerViewModel.cs` is now 235 lines and delegates status, settings projection,
  tray options, channel collection, master state and source recovery.
- `MainWindow.xaml.cs` is now 212 lines and delegates tray sync, drag/drop,
  app-panel animation, rename behavior and master-strip logic.
- `App.xaml` is now a 14-line merged-dictionary shell over focused resource files.
- `MainWindow.xaml` now keeps shell layout inline but uses named templates and
  `MasterStripControl` for repeated strip markup.
- `AudioHQ.Tests` contains 10 focused test files covering hardware-free logic.

The most important structural theme is to split orchestration, persistence, UI
events, and visual resources into smaller named units without changing behavior.

## Checkpoints

| ID | Checkpoint | Progress | Target outcome |
| --- | --- | ---: | --- |
| CP0 | Audit and planning baseline | 100% | Inventory hotspots, stale docs, likely refactor boundaries, and risks. |
| CP1 | Documentation baseline cleanup | 100% | Bring `README.md`, `docs/ARCHITECTURE.md`, and this plan fully in sync with the current app mixer and tray behavior. |
| CP2 | Safety net tests | 100% | Add a test project for settings serialization, EQ model behavior, app-mixer ordering, and pure helper logic. |
| CP3 | Root view-model decomposition | 100% | Split `MixerViewModel` into device/source recovery, channel collection, settings persistence, and tray option coordination helpers. |
| CP4 | Channel activation service | 100% | Move COM error mapping, fresh device resolution, activation cleanup, and retry-budget behavior out of `ChannelViewModel`. |
| CP5 | Window code-behind cleanup | 100% | Extract tray synchronization, rename helpers, drag/drop helpers, and app-panel animation into focused classes or behaviors. |
| CP6 | XAML resource split | 100% | Split `App.xaml` into merged dictionaries for colors/tokens, shared controls, faders, app mixer, and window shell styles. |
| CP7 | Main window template split | 100% | Move app mixer, master strip, and channel strip markup into user controls or data templates with stable resource names. |
| CP8 | Core pipeline cleanup | 100% | Extract `OutputChannel` to its own file and evaluate folding the CLI onto `MirrorEngine` to reduce duplicate loopback logic. |
| CP9 | Platform/tooling modernization | 100% | Decide whether to move from `net7.0` to `net8.0`, add analyzers, and document the supported runtime/build matrix. |
| CP10 | Final regression pass | 100% | Build, smoke-test audio flows, verify screenshots/layout, update docs/changelog, and close the refactor cycle. |

## Bugs And Risks Found During Audit

- `README.md` described a manual app-mixer refresh button and window-focus
  refresh path after the UI moved to automatic refresh while the panel is open;
  this was corrected in the v0.6.6 planning pass.
- `docs/ARCHITECTURE.md` was reviewed against the current app mixer, tray,
  persistence and watchdog behavior during CP1.
- `README.md` and `docs/ARCHITECTURE.md` diagrams were normalized to plain ASCII
  after mojibake box-drawing characters were found.
- `Directory.Build.props` now matches `CLAUDE.md` on patch rollover: patch bumps
  happen every edit batch, `0.x.9` rolls to `0.(x+1).0`, and major bumps require
  explicit user approval.
- Automatic app-session refresh is a UI-thread `DispatcherTimer`; if session
  enumeration ever blocks, the UI can stutter. CP5 should consider a measured,
  cancellable refresh path only if profiling shows it is needed.
- `MixerSettings.Save()` writes directly to `settings.json`. CP2/CP3 should
  decide whether atomic replace is worth adding to protect against interruption
  during write.
- CP2 now covers EQ settings, EQ view-model normalization/reset, settings
  serialization, status state and app-mixer ordering/persistence helper behavior.
- CP3 extracted `MixerStatusViewModel`, `MixerSettingsProjection`,
  `MixerTrayOptionsViewModel`, `MixerChannelCollectionViewModel`,
  `MixerMasterViewModel` and `MixerSourceRecoveryViewModel`. `MixerViewModel`
  now coordinates these owned pieces while preserving the existing window binding
  surface.
- CP4 extracted `ChannelActivationService` and `ChannelRetryBudget`, with tests
  covering COM status mapping and retry-budget semantics.
- CP5 extracted `MainWindowTraySync`, `WindowIconFactory`, `AppPanelAnimator`,
  `AppRowDragController`, `ChannelDragDropController` and `RenameTextBoxController`.
  `MainWindow.xaml.cs` now delegates tray/taskbar sync, panel animation, drag/drop
  and rename mechanics to named helpers.
- CP6 split `App.xaml` into merged dictionaries for tokens, shared strip/dialog
  styles, and app-mixer styles while preserving resource keys.
- CP7 moved app-mixer row and channel strip markup into named data templates and
  extracted the source master strip into `MasterStripControl`.
- CP8 moved `OutputChannel` to its own file and updated `AudioHQ.Cli` to use
  `MirrorEngine` instead of `LoopbackMirror`.
- CP9 enabled SDK analyzers, fixed/suppressed the resulting warnings, and
  deferred `net8.0` migration because this workspace only has .NET SDK 7.0.
- CP10 regression is complete. `dotnet test AudioHQ.sln` passes with analyzers
  enabled, and the user confirmed live desktop/audio behavior on 2026-07-09.
- `MixerSettingsProjection` now owns the pure mapping from live mixer state into
  `MixerSettings`. File I/O timing remains in `MixerViewModel`.

## Proposed Order

1. CP1 first, because docs are already stale and will be the contract for the
   rest of the work.
2. CP2 next, before large moves, so behavior has executable coverage.
3. CP3 through CP7 in small batches, each with build verification and docs.
4. CP8 and CP9 after the UI/ViewModel cleanup, because they touch broader
   runtime and architecture decisions.
5. CP10 as the release-quality validation pass.

## Definition Of Done For Each Checkpoint

- Version bumped according to `CLAUDE.md`.
- `CHANGELOG.md` updated.
- `docs/ARCHITECTURE.md` reviewed and updated when behavior or structure changed.
- `docs/REFACTOR_PLAN.md` checkpoint percentage updated.
- `dotnet build AudioHQ.sln` passes, or the reason it cannot be run is recorded.
- Manual visual/audio verification notes are added when the checkpoint changes UI
  layout or audio behavior.
