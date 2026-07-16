# Changelog

All notable changes to AudioHQ. One entry per version bump
(see the repository versioning conventions - patch bump on every edit batch).

## 0.5.19 - 2026-07-16

### Changed
- Removed the twelve migration-only color aliases now that nothing uses them. Every
  brush in the app is reached through its theme role, leaving one way to do it.

## 0.5.18 - 2026-07-16

### Changed
- Windows no longer carry their own hard-coded text sizes, colors, corner radii or
  margins - every one now comes from the shared theme, so the look is adjustable from
  one place. Options and EQ share one title, section-label and check-box style instead
  of repeating the same settings.
- Spacing is now on a consistent 6px rhythm. The Options and EQ dialogs had drifted to
  ad-hoc gaps (the three Options section labels each used a different one), so a few
  gaps shift by 1-4px and the sections line up evenly. The main window is unchanged.
- Corner radii that sat just off the scale are snapped onto it: the ON/MUTE/EQ pills go
  from 7 to 8, and the small icon buttons from 5 to 6.

## 0.5.17 - 2026-07-16

### Changed
- The channel fader's green is now the same green as the ON pill and the app-mixer
  faders (a slightly deeper green than before). It had drifted to its own lighter
  shade. Boost amber and max-boost red are unchanged.
- The tray icon's and taskbar overlay's "active" dot now use the app's green instead
  of the generic Windows lime green, so every "on" indicator matches.
- Colors, and the app-mixer slide timing, are no longer duplicated in C#: they all
  read from the shared theme.

## 0.5.16 - 2026-07-16

### Changed
- Internal reorganization: each control family now has its own style file under
  `Resources/Controls/` instead of two large mixed ones, and every style reads its
  colors from the shared theme. The main window is pixel-for-pixel unchanged.
- The two near-identical scrollbar styles are now one. Scrollbars outside the app
  mixer (combo dropdowns) pick up the polish the app-mixer one already had: the thumb
  dims slightly on hover and while dragging.

## 0.5.15 - 2026-07-16

### Changed
- Internal groundwork with no visible change: colors, text sizes, spacing, corner radii
  and animation timing now have a single source of truth under `Resources/Theme/`.
  Every color hex is defined once, and the previous style keys forward to it.

## 0.5.14 - 2026-07-16

### Changed
- Internal cleanup with no behavior change: the UI-thread marshaling snippet that was
  hand-copied into three callbacks now lives in one shared `UiDispatcher.Post` helper.

## 0.5.13 - 2026-07-16

### Changed
- A muted output channel now stays muted after a restart, matching how its volume is
  already remembered. Settings files written before this change still load and their
  channels come back unmuted.

## 0.5.12 - 2026-07-16

### Fixed
- Volume, EQ and rename edits no longer wait for a clean exit to be saved. They are
  now written about two seconds after you stop making changes, and immediately when
  the window loses focus or Windows logs off - so an edit made while AudioHQ sits in
  the tray survives a crash or a forced shutdown. Dragging a fader still writes only
  once, when you let go.
- settings.json is now written atomically (temp file, flushed to disk, then swapped
  in). Losing power or killing the app mid-save can no longer truncate it; the
  previous settings stay intact until the new file is complete.

## 0.5.11 - 2026-07-16

### Fixed
- The per-app mixer no longer leaks a WASAPI device handle on every refresh tick.
  AppSession now roots only its own SimpleAudioVolume (not the source device),
  and the snapshot method disposes the device in a finally block after extracting
  all sessions. Measured: ~1800 handles an hour with the panel open, none of which
  the garbage collector reclaimed.
- Hot-plugging devices during long sessions no longer retains removed endpoints:
  the two recovery paths that dropped a device from the source list without
  releasing it now dispose it.

## 0.5.10 - 2026-07-16

### Fixed
- One failing output can no longer interrupt audio to the other outputs. A channel
  that throws while receiving audio is now logged once and skipped instead of
  unwinding the capture callback and cutting every other output with it.
- The source-loss watchdog can no longer miss a capture-stopped transition: the
  capturing flag is now published correctly across threads.

## 0.5.9 - 2026-07-16

### Fixed
- Repeatedly failing to activate an output no longer accumulates leaked Windows audio
  objects. When a device rejects both event-sync and push mode, the half-built channel
  now releases its audio client instead of leaking one per attempt, which auto-retry
  used to multiply. Disposing a channel also survives a device that has already gone
  away.

## 0.5.8 - 2026-07-16

### Fixed
- Stopping the mirror now always finishes cleanly, even when the source device has
  already been unplugged or disabled. Every teardown step is guarded individually,
  so a driver that throws can no longer leave the engine stuck holding a dead
  capture, which also keeps the source-loss watchdog able to recover.

## 0.5.7 - 2026-07-15

### Fixed
- The app mixer now restores its exact layout state after restart: attached and
  expanded, attached and collapsed, or detached. The chosen state also survives
  minimizing to and restoring from the system tray.

## 0.5.6 - 2026-07-15

### Fixed
- Saved output channels now recover automatically when Windows recreates an HDMI or
  USB endpoint under a new id, using a unique last-known device-name match without
  changing the channel's label, gain, EQ, order, ON intent, or tray focus.
- If the replacement endpoint was already added manually, startup migration keeps
  the original configured strip and removes the redundant replacement strip.

### Added
- Added a persisted "Launch minimized to system tray" option that starts AudioHQ
  without showing its main window, including when launched with Windows.

## 0.5.5 - 2026-07-12

### Changed
- Removed local-only development metadata from the public repository while keeping
  it available in ignored local files.
- Expanded `.gitignore` coverage for local editor/tool state and generated test
  results while keeping the full test source project tracked.
- Reworded public documentation to reference neutral repository conventions.

## 0.5.4 - 2026-07-12

### Changed
- Rewrote the GitHub README around the current routing, detachable app mixer,
  per-output EQ/presets, recovery, persistence, and coordinated-window features.
- Replaced the README hero image with the current detached-mixer and EQ screenshot.
- Prepared the self-contained Windows portable release and release notes.

## 0.5.3 - 2026-07-12

### Changed
- Channel strip area now scrolls horizontally when more than 12 strips are added;
  the window auto-grows to show up to 12 output channels at once without a scrollbar.

## 0.5.2 - 2026-07-12

### Changed
- Owned EQ and Options windows now follow the main window at their current relative
  offsets, making the visible AudioHQ windows move as one group. Manually repositioning
  a child establishes its new offset for later main-window movement.

## 0.5.1 - 2026-07-12

### Changed
- Main-window close now owns the whole application lifetime: with close-to-tray off,
  closing it exits all owned windows; with tray behavior enabled, the main window and
  every visible Options, EQ, and detached mixer window hide as one set.
- Restoring from the tray reopens exactly the owned windows that were visible before
  hiding and returns focus to the previously active window. Options and EQ are now
  reusable owned modeless windows instead of blocking modal dialogs.

## 0.5.0 - 2026-07-12

### Changed
- Reset the requested application version line to 0.5.0.
- Changed the mandatory version cadence to 100 patch releases per minor:
  `0.5.0` through `0.5.99`, followed by `0.6.0`.

## 0.10.0 - 2026-07-12

### Changed
- EQ preset selection now loads immediately without a separate Load action.
- Modified presets retain their original selection and display `Name (not saved)`.
- Replaced Load with a Reset action that enables only for unsaved edits and restores
  the active preset's saved values, including the built-in Default. Removed the
  redundant bottom Reset button. Enable EQ is included in dirty-state detection.

## 0.9.9 - 2026-07-12

### Changed
- EQ preset save action now becomes `Overwrite preset` when editing a selected
  non-Default preset with an empty name field, and returns to `Save preset` when
  a new name is entered. The action is green and Delete is red.

## 0.9.8 - 2026-07-12

### Changed
- Reversed both mixer dock-action arrows: Detach now points left and Attach points right.

## 0.9.7 - 2026-07-12

### Changed
- Replaced the mixer dock glyphs with a matched custom pair: Detach is a dim
  outlined panel with an outward right arrow; Attach is a bright filled panel
  with an inward left arrow.

## 0.9.6 - 2026-07-12

### Changed
- Attaching the floating mixer now restores it immediately as a fully expanded
  docked panel instead of returning it collapsed.
- Replaced the custom attach/detach paths with the standard Windows Segoe Fluent
  `DockLeft` and `NewWindow` glyphs.

## 0.9.5 - 2026-07-12

### Changed
- Detached mixer now uses the same native draggable title bar as AudioHQ, Options,
  and EQ, titled `AudioHQ - Mixer`. Its X button hides the mixer so the main chevron
  can show it again, while attach and application shutdown close the host permanently.

## 0.9.4 - 2026-07-12

### Changed
- The main window's mixer chevron now hides and shows the floating mixer while it
  remains detached. Hiding pauses app-session refresh; showing refreshes and focuses it.

## 0.9.3 - 2026-07-12

### Changed
- Reduced the detached mixer's no-scroll maximum from 12 to 10 app rows.
- Maximum height now uses each row's actual rendered height when available, removing
  the cumulative empty gap below the last app while retaining the handle inset.

## 0.9.1 - 2026-07-12

### Fixed
- Detached mixer now opens fully expanded by default for all current apps, capped
  at 12 visible rows and the available screen height. A shorter height is retained
  only after the user deliberately drags the resize handle upward.

## 0.9.0 - 2026-07-12

### Fixed
- The detached mixer now sums every generated app-row container when calculating
  its maximum height, avoiding the scroll viewer's constrained viewport measurement
  that incorrectly limited the panel to approximately one visible row.

## 0.8.9 - 2026-07-12

### Fixed
- The detached mixer's maximum height now uses the app rows' measured rendered
  height instead of a fixed estimate, preventing an unnecessary scrollbar when
  the panel is fully expanded and the screen has enough room.

## 0.8.8 - 2026-07-12

### Fixed
- Centered the main window on first launch and restored its last valid normal
  position on later launches, with virtual-desktop bounds validation.
- Fixed the detached mixer's initial natural height so all current app rows are
  visible up to the screen limit, and made its resize indicator thinner.

## 0.8.7 - 2026-07-12

### Added
- Added a centered bottom resize handle to the detached mixer. It can shorten the
  panel or restore it up to the exact height needed for all visible app rows, but
  cannot create empty space below them.

## 0.8.6 - 2026-07-12

### Changed
- Detached mixer height now follows its visible app rows, capped by the available
  work area, instead of using a manually resized persisted height.
- Doubled the app mixer's right content inset to 12 px and removed the bottom
  resize thumb that produced a white strip below the rounded panel.

## 0.8.5 - 2026-07-12

### Fixed
- Kept the app mixer's view-model binding when moving the panel into its detached
  window, so active application rows remain visible after detaching or restarting.

## 0.8.4 - 2026-07-12

### Added
- Added a persistent dock/undock control to the app mixer. The detached mixer is
  top-aligned to the left of the main window, opens at a taller default height,
  and can be resized vertically from its bottom edge.
- The collapsed mixer rail now reveals and focuses the detached mixer.

## 0.8.3 - 2026-07-09

### Changed
- Closed the refactor plan after automated tests and user-confirmed live app
  smoke verification.

## 0.8.2 - 2026-07-09

### Changed
- Updated the refactor checkpoint plan for the final automated regression pass.

## 0.8.1 - 2026-07-09

### Changed
- Added shared .NET SDK analyzer settings in `Directory.Build.props`.
- Fixed production analyzer warnings and suppressed test-only naming/exception
  warnings that conflict with xUnit and COM-status mapping tests.
- Documented that the `net8.0` migration is deferred because this workspace only
  has .NET SDK 7.0 installed.

## 0.8.0 - 2026-07-09

### Changed
- Moved `OutputChannel` from `MirrorEngine.cs` into its own core file.
- Updated the CLI tester to use `MirrorEngine` instead of the older
  `LoopbackMirror` path.

## 0.7.9 - 2026-07-09

### Changed
- Split `MainWindow.xaml` markup by moving app-mixer rows and channel strips into
  named data templates, and extracted the master strip into `MasterStripControl`.

## 0.7.8 - 2026-07-09

### Changed
- Split the large application resource dictionary into `Resources/Tokens.xaml`,
  `Resources/StripStyles.xaml`, and `Resources/AppMixerStyles.xaml`, keeping
  existing resource keys and merged-dictionary order.

## 0.7.7 - 2026-07-09

### Changed
- Extracted main-window tray/taskbar synchronization, app-panel animation,
  app-row drag reordering, channel drag reordering, and shared rename text-box
  behavior into focused UI helpers.

## 0.7.6 - 2026-07-09

### Changed
- Extracted channel activation, activation-failure status mapping, failed-output
  cleanup, and auto-reactivation retry budgeting from `ChannelViewModel` into
  focused helpers.

### Added
- Added tests for channel activation status mapping and retry-budget behavior.

## 0.7.5 - 2026-07-09

### Changed
- Extracted source selection, device-list refresh, fallback recovery, and
  sleep/resume restart handling from `MixerViewModel` into
  `MixerSourceRecoveryViewModel`.

## 0.7.4 - 2026-07-09

### Changed
- Extracted master-strip rename and source endpoint volume/mute state from
  `MixerViewModel` into `MixerMasterViewModel`, with the main window binding to
  `Master.*`.

## 0.7.3 - 2026-07-09

### Changed
- Extracted curated output-channel collection, add/remove/reorder, and
  tray-focus selection from `MixerViewModel` into
  `MixerChannelCollectionViewModel` while preserving the existing root bindings.

## 0.7.2 - 2026-07-09

### Changed
- Extracted tray and startup option coordination from `MixerViewModel` into
  `MixerTrayOptionsViewModel`, keeping persistence and startup registration
  behavior unchanged.

### Added
- Added tests for tray option persistence and Run-with-Windows synchronization.

## 0.7.1 - 2026-07-09

### Changed
- Extracted the pure settings projection from `MixerViewModel.Save()` into
  `MixerSettingsProjection`, leaving save timing and file I/O behavior unchanged.

### Added
- Added a focused test for settings projection so future root view-model
  decomposition can keep persisted fields stable.

## 0.7.0 - 2026-07-09

### Added
- Added `AppMixerLayout`, a pure app-mixer ordering helper with tests for pinning,
  drag moves, saved-order replay and absent-row persistence.

### Changed
- `AppMixerViewModel` now delegates row ordering and persisted layout projection
  to `AppMixerLayout`, completing the CP2 safety-net test checkpoint.

## 0.6.9 - 2026-07-09

### Changed
- Extracted mixer notification bubble state into `MixerStatusViewModel` and bound
  the main window status toast to that focused view-model.
- Added tests for status message/severity behavior.

## 0.6.8 - 2026-07-09

### Added
- Added `tests/AudioHQ.Tests`, an xUnit safety-net project covering EQ settings,
  EQ view-model normalization/reset behavior and settings serialization.

### Changed
- Documented the test project in README and architecture docs, and marked CP2
  as partially complete in `docs/REFACTOR_PLAN.md`.

## 0.6.7 - 2026-07-09

### Fixed
- Completed the CP1 documentation baseline by normalizing README and architecture
  diagrams to plain ASCII.
- Corrected the versioning comment in `Directory.Build.props` so it matches the
  project rule for automatic patch-to-minor rollover.
- Marked CP1 complete in `docs/REFACTOR_PLAN.md`.

## 0.6.6 - 2026-07-09

### Added
- Added `docs/REFACTOR_PLAN.md` with a technical audit, checkpointed refactor
  plan, per-checkpoint progress percentages and known documentation risks.

### Fixed
- Updated the README app-mixer feature text so it no longer mentions the removed
  refresh button or window-focus refresh path.
- Corrected the architecture document's known-limitations section to reflect
  that mixer state is now persisted across runs.

## 0.6.5 - 2026-07-09

### Changed
- App mixer now refreshes its app-session list automatically while the panel is
  open, so newly playing apps appear without the manual refresh button.
- Replaced the app mixer refresh icon with a `MIXER` header above the app rows.

## 0.6.4 - 2026-07-09

### Fixed
- Collapsed app mixer rail keeps the small left inset to the master strip via
  the shared `MainContentMargin` resource instead of a literal panel margin.

## 0.6.3 - 2026-07-09

### Fixed
- Collapsed app mixer rail now has the same 6px gap to the master strip as it
  has to the window edge, removing the extra main-content left inset.

## 0.6.2 - 2026-07-09

### Fixed
- Collapsed app mixer rail no longer keeps the hidden panel's left margin, so
  the chevron button has matching spacing on both sides.

## 0.6.1 - 2026-07-09

### Fixed
- App mixer layout now uses one consistent panel width resource matching its
  content width and margins, so the right scrollbar is not clipped.
- Removed the mirrored left scrollbar spacer that made the left inset appear
  larger than the intended 12px medium spacing.

## 0.6.0 - 2026-07-09

### Fixed
- App mixer channel list now uses the medium 12px spacing again on the left side
  and bottom edge.

## 0.5.9 - 2026-07-09

### Fixed
- App mixer channel list now uses a smaller left spacer and bottom inset, reducing
  the visible left and lower padding around app rows by half.

## 0.5.8 - 2026-07-09

### Fixed
- App mixer scrollbar now keeps the same 6px gap on its left side as on the
  panel's right side, so it no longer touches mixer rows.
- App mixer panel spacing now uses shared XAML resources for the panel margin,
  inner padding, row padding and row gaps instead of repeated local pixel values.

## 0.5.7 - 2026-07-08

### Fixed
- App mixer rows are now grouped by stable application identity, so browsers and
  Electron apps that expose multiple WASAPI sessions/processes no longer appear
  as duplicate rows.
- App mixer pin state and row order are persisted in `settings.json` and kept
  even while an app is absent, so a pinned app returns to its remembered position
  when it starts playing again.

## 0.5.6 - 2026-07-06

### Fixed
- Channels no longer die permanently after PC sleep/resume (the "TV channel
  stops working until removed and re-added" bug). Root causes fixed:
  - Cached MMDevice COM objects were reused after resume even though Windows had
    invalidated them. Every activation (channel outputs and the capture source)
    now resolves a fresh device by endpoint id (`AudioDevices.FindRenderById`);
    channels no longer hold a device instance at all, only the id + a presence flag.
  - A dead output was never detected: `WasapiOut.PlaybackStopped` is now
    observed (`OutputChannel.PlaybackStopped`), so a channel whose device died
    shows "Reconnecting..." and is re-opened automatically.
  - Deactivation on device loss erased the channel's ON state. Channels now keep
    a persistent "wants active" intent; mechanical stops (device loss, engine
    restart, sleep, becoming the source) suspend without clearing it, and the
    3 s watchdog auto-reactivates wanted channels (retry budget 3, reset when
    the device reappears or after a resume).
- Resume detection: `SystemEvents.PowerModeChanged` plus a clock-jump fallback
  (Modern Standby machines often miss the event). On resume the app clears the
  unstartable-source list, replaces all cached device instances with fresh ones
  (master volume talks to a live AudioEndpointVolume again), restarts capture
  and keeps retrying channel re-opens for ~30 s while devices come back staggered.
- Audio callback stalls: the capture thread no longer takes a lock or writes to
  the log file while fanning out to outputs (immutable snapshot instead), so a
  slow disk or a UI-thread add/remove can no longer stutter all channels.
- EQ adjustments no longer click: `EqualizerProvider.Configure` updates biquad
  coefficients in place (filter state survives) instead of rebuilding the bank
  on every fader tick; rebuilds only happen on band-count/low-pass topology
  changes, and per-tick log spam is gone (structural changes only).
- `AdaptiveResampler` diagnostic logging removed from the render hot path (it
  did synchronous file I/O on the WASAPI pull thread); it also returns silence
  instead of 0 samples if the resampler momentarily produces nothing, so
  WasapiOut can never mistake a hiccup for end-of-stream.
- Resource leaks: the 3 s device poll no longer leaks an MMDevice COM wrapper
  per known device per tick (duplicates disposed); OutputChannel disposes its
  device; EqWindow unhooks its band-collection event handlers on close (each
  opened editor used to stay rooted by the channel's EQ model).
- Robustness: `RestartEngine` catches non-COM exceptions too (a misbehaving
  device could pop the crash dialog every 3 s from the watchdog); the whole
  watchdog tick is exception-guarded; master volume/mute reads and writes are
  guarded against a device dying between ticks; OutputChannel teardown of a
  dead device can no longer throw out of Dispose.

## 0.5.5 - 2026-07-06

### Added
- EQ "Bass-only (low-pass)" high-cut: a cascade of low-pass biquads applied on
  top of the peaking bands. Adjustable cutoff (30-500 Hz) and slope (12 or
  24 dB/oct). Passes the deep low end and rolls off everything above the cutoff
  - the correct tool for a bass shaker (keep the rumble, kill the rest), where
  peaking bells could only dip and never fully remove a range. Persisted per
  channel (`EqSettings.LowPassEnabled/LowPassHz/LowPassSlope`).

### Changed
- EQ band faders now cut to -36 dB (was -12). Range is asymmetric: +12 dB boost
  / -36 dB cut, so a band can be taken nearly out of the mix. `EqBands.MaxGainDb`
  split into `MaxBoostDb` (12) and `MaxCutDb` (36); the response-curve baseline
  and limits in the editor were reworked for the off-centre 0 dB.

## 0.5.4 - 2026-06-29

### Fixed
- Without a scrollbar the app mixer panel had 12px left margin vs 6px right
  (asymmetric). Reduced the inner Grid left margin from 12px to 6px so both
  sides are equal (6px) with no scrollbar, and equal (12px) when it is visible.
  AppPanelWidth adjusted from 244 to 238 to match (6+226+6).

## 0.5.3 - 2026-06-29

### Fixed
- App mixer scrollbar caused asymmetric row padding: scrollbar took 6px on the
  right while the left side had no matching gap. Fixed with a three-column
  AppScrollViewer layout (left spacer | content | scrollbar) where the spacer
  mirrors the scrollbar width and visibility, keeping rows centred at all times.
- App mixer scrollbar was always visible even when all rows fit in the viewport.
  Changed VerticalScrollBarVisibility from Visible to Auto so the bar (and the
  matching left spacer) collapse when there is nothing to scroll.

## 0.5.2 - 2026-06-29

### Fixed
- App mixer scrollbar thumb overflowed the 6px rail on both sides because the WPF
  `ScrollBar` control has a default `MinWidth` (~18px) that overrides the `Width="6"`
  setter, making the control 18px wide with the thumb stretching to fill it.
  Fix: added `MinWidth="0"` to AppScrollBar style and gave the thumb's Border an
  explicit `Width="6" HorizontalAlignment="Center"` so it always matches the 6px rail
  regardless of the allocated scrollbar width.

## 0.5.1 - 2026-06-29

### Fixed
- Main content area had 0px left padding vs 12px right padding. Changed main
  DockPanel margin from `0,12,12,12` to `12,12,12,12` for symmetric spacing.

### Added
- Focused channel selection (the green dot / tray selector) is now persisted to
  settings.json and restored on restart. Added `Focused` field to `ChannelDefinition`;
  `ToggleFocusChannel` calls `Save()` after each change.
- Taskbar button icon now shows the same green dot overlay as the tray icon when
  the focused channel is active, using a 32x32 BitmapSource pre-built at startup.

## 0.5.0 - 2026-06-29

### Fixed
- App mixer scrollbar had asymmetric gaps: 6px on the left (row padding) but 12px
  on the right (outer Grid right margin). The previous negative-margin trick on the
  ScrollViewer had no effect because the Grid clips children to its explicit Width.
  Fix: widened the inner Grid from 220 to 226px and reduced its right margin from
  12px to 6px so the scrollbar right edge sits 6px from the panel edge, matching the
  left side. Removed the no-op negative margin from the ScrollViewer.

## 0.4.9 - 2026-06-29

### Changed
- App mixer scrollbar (AppScrollBar) narrowed from 10px to 6px, matching the
  global slim scrollbar width.

## 0.4.8 - 2026-06-29

### Fixed
- App mixer scrollbar thumb was wider than the scrollbar rail due to WPF's default
  Thumb MinWidth overriding the layout constraint. Added `MinWidth="0"` to the Thumb
  element and removed the 2px horizontal margins so the thumb fills the rail exactly.

## 0.4.7 - 2026-06-29

### Added
- Tray-focus selector: a small dot button in each channel header acts as a radio
  selector (only one channel at a time). When a channel is focused, the tray icon
  shows a green dot overlay if that channel is ON, and the plain icon when it is
  OFF. Middle-clicking the tray icon toggles the focused channel on/off.

## 0.4.6 - 2026-06-23

### Fixed
- App mixer scrollbar now overlays the content (HorizontalAlignment="Right") instead
  of occupying a separate column. The right gap equals the inner Grid's right margin
  (same 12px as the left content margin). Removed the 6px left/right margins that
  were pushing the scrollbar out of the column alignment.

## 0.4.5 - 2026-06-23

### Fixed
- App mixer scrollbar clipped: inner Grid had Width="244" matching the Border's
  animated width, but WPF adds margin (12px each side) on top of Width, so the
  Grid overflowed 12px to the right and the scrollbar was hidden behind the
  Border's ClipToBounds. Changed Width to 220 (244 - 2*12) so the Grid fits
  exactly within the Border when fully open.

## 0.4.4 - 2026-06-23

### Fixed
- Right gap when panel is closed: AppPanel.Margin.Right now animates 0->6 on
  open and 6->0 on close (ThicknessAnimation in code-behind). Initial XAML value
  changed to "6,0,0,0" so the closed state has a single S=6 gap to the main
  content (AppPanel.Margin.Left) rather than L=12.
- Chevron icon off-center: path data changed from "M15 5l-7 7 7 7" (origin at
  x=15) to "M7 0 L0 7 L7 14" (origin at 0,0). Both natural and 180-rotated
  states are now symmetrically centered in the Viewbox.

## 0.4.3 - 2026-06-23

### Changed
- Toggle/panel spacing: AppMixerRegion left 12->6 (S), AppPanel Margin 6,0,6,0 (S
  each side of the card when open), main DockPanel left 6->0 (AppPanel right margin
  now owns the 6 px gap to the channels).

## 0.4.2 - 2026-06-23

### Changed
- Toggle button spacing: left edge to window = L (12 px); right edge to first
  channel card = S (6 px). Achieved by zeroing all internal margins on the toggle
  button and AppPanel, removing the AppMixerRegion right margin, and setting the
  main DockPanel left margin to 6 px.

## 0.4.1 - 2026-06-23

### Changed
- App mixer scrollbar: permanent #3C4E68 track always visible; white thumb appears
  only when content overflows (custom AppScrollViewer + AppScrollBar styles). Track
  column = 12 px = S/2 (3 px) padding + 6 px rail + S/2 (3 px) padding.
- Drag handle: added 3 px (S/2) right margin, separating it from the app icon.
- App icons: removed #22FFFFFF background overlay; icon Image now direct (no Border
  wrapper). Size increased 26 -> 30 px; column width updated to match.
- AudioHQ removed from its own mixer list (filtered by current process ID on refresh).

## 0.4.0 - 2026-06-23

### Changed
- Spacing system normalised to two sizes throughout the app: S=6 px, L=12 px.
  All paddings and margins now use one of these two values only.
  - Outer window DockPanel margin: 14 -> 12 (L)
  - Header StackPanel bottom margin: 10 -> 12 (L)
  - Notification border top margin: 8 -> 6 (S)
  - AppMixerRegion top/bottom margin: 14 -> 12 (L); left: 4 -> 6 (S)
  - Inner panel grid margin: 12,10,12,10 -> 12,12,12,12 (all L) - fixes last-row
    clip by the panel's 12 px corner radius
  - Refresh button bottom margin: 8 -> 6 (S)
  - App row padding: right 8 -> 6 (S, now equal left/right); bottom margin 4 -> 6 (S)
  - Name TextBlock horizontal margins: 7,4 -> 6,6 (S both sides)
  - Pin button right margin: 3 -> 6 (S)
  - Volume slider margin: 7,4 -> 6,6 (S both sides)
- Drag-handle dots icon: Canvas height 48 -> 18 px so the dots centre vertically
  at app-icon height instead of sitting near the top of the row.
- Scrollbar track colour: ButtonBrush (#2D3545) -> #3C4E68 for better contrast
  against CardBrush (#222834). Left margin: 4 -> 6 px (S).

## 0.3.9 - 2026-06-23

### Changed
- App mixer scrollbar: styled dark - ButtonBrush track with 4 px top/bottom insets,
  TextBrush thumb; 4 px left margin separates the rail from row content; right side
  is already padded by the panel grid margin (12 px). Applies globally.
- App row right controls: pin button now has a 3 px right margin before the vol%
  text for visual separation (order remains: pin - vol% - mute).

## 0.3.8 - 2026-06-23

### Changed
- App mixer panel: removed the "Apps" header label to gain one extra row of height.
- App mixer panel: scrollbar height constraint fixed - the panel now properly fills
  the window height so `VerticalScrollBarVisibility="Auto"` triggers when needed.
- Toggle button: chevron now centered in a Viewbox (equal margins, crisp at all DPIs).
- App row layout restructured: left column holds a **pin button** (top) and a
  **drag-handle** dots icon (bottom); app icon enlarged to 26x26 px; mute toggle
  remains on the right; pin button moved from the right to the left column.
- Drag-and-drop animation improved: dragging now shows a rounded ghost snapshot of
  the row (with a drop shadow) that follows the cursor; the source row is dimmed
  to 35% opacity while dragging; the drop target row shows a blue top-border
  insertion indicator; a custom `DragAdorner` (new class) drives the ghost.

## 0.3.7 - 2026-06-22

### Added
- Per-app mixer rows can now be **pinned to the top** (pin button) and
  **dragged to reorder** (grab a row by its icon). Pinned rows are kept above
  unpinned ones and get a faint blue tint; reordering is confined to a row's pin
  group. Rows slide smoothly to their new position via `FluidMoveBehavior`
  (new dependency: `Microsoft.Xaml.Behaviors.Wpf`). The order survives refreshes -
  a refresh updates rows in place and appends only newly-seen apps at the bottom.

### Changed
- The app-mixer rail chevron direction is reversed: it points left (`<`) when the
  panel is collapsed and right (`>`) when it is open.

## 0.3.6 - 2026-06-22

### Added
- Slide-out per-application mixer on the left. A chevron rail opens a panel that
  lists the apps currently playing on the default output device (the same set as
  the Windows volume mixer), each with its icon, a horizontal volume slider and a
  mute toggle. These drive each app's own Windows volume - fully independent of
  the mirror strips and the MASTER. The list refreshes when the panel opens, when
  the window is brought to the front, and from a manual refresh button. New engine
  module `AppSession` / `AppSessions` (WASAPI Audio Session API), with UI in
  `AppMixerViewModel` / `AppSessionViewModel` and `AppIcon` for icon extraction.

## 0.3.5 - 2026-06-18

### Changed
- The engine status line is now a notification toast: a rounded, tinted frame
  with a dismiss (X) button instead of loose red text. Informational notices
  (e.g. "Source restored to ...", "Source switched to ...") show a blue frame so
  they no longer read as an error; genuine failures (source locked, no device)
  keep a red frame. Click the X to dismiss it.

## 0.3.4 - 2026-06-16

### Fixed
- The chosen source now survives a PC restart. When AudioHQ starts before the
  preferred output has finished connecting (e.g. Bluetooth earbuds right after
  boot), it falls back to a working device as before, but the watchdog now
  switches back to the chosen source as soon as it reappears.
- A temporary fallback source is no longer written to settings.json, so it can
  never quietly overwrite the user's real source preference. Only an explicit
  source pick changes the saved choice. A source that refuses to start is
  remembered and not retried until it disconnects and reconnects.

## 0.3.3 - 2026-06-14

### Changed
- The master fader keeps its own 0-100% range (100% is the maximum), but the
  green 100% line and the "100" label are now pinned to the thumb centre at full
  scale, so at 100% the thumb sits on the line and overhangs slightly above it
  (like the channel thumbs at their max) instead of the line floating above the
  handle.
- The EQ sliders glyph no longer shows a tooltip bubble on hover; it recolours
  (to the accent blue) instead to signal it is clickable.
- Faders no longer draw the dotted keyboard-focus rectangle.

## 0.3.2 - 2026-06-13

### Changed
- The EQ pill's settings glyph is now a sliders/"tune" icon, distinct from the
  app's options gear (the editor it opens is the EQ, not general settings).

### Fixed
- The master strip's green line is now centred on the fader thumb and tracks it
  as the volume moves (it is wider than the thumb, so it peeks out either side),
  instead of sitting above the handle.

## 0.3.1 - 2026-06-13

Follow-up polish to the EQ/options dialogs and the master strip.

### Changed
- The Options dialog now docks beside the app (right, or left if there is no
  room) like the EQ editor, and its source/latency pickers and Close button use
  the dark theme instead of white Windows chrome.
- The EQ settings gear now lives inside the EQ pill at its right edge (clicking
  the pill body toggles EQ; the gear opens the editor) instead of as a separate
  button beside it.
- The EQ preset picker now reflects the live curve: it shows "Default" when the
  curve is flat and "Custom (not saved)" once any fader or Q knob is moved away
  from a saved preset. The state is derived from the curve, so it survives
  closing and reopening the editor.

### Fixed
- The master strip's green "100%" line is now placed from the fader's rendered
  thumb position (extrapolated to full scale), so it sits on the thumb at 100%
  instead of floating above it.

## 0.3.0 - 2026-06-13

EQ refinements: per-band bandwidth control, in-app theming and a docked editor.

### Added
- Per-band Q (`EqSettings.QValues`): a rotary knob under each fader (new `Knob`
  control) sets how rounded or sharp that band's bell is. Drag up for a narrower,
  sharper peak, down for a wider, rounder one; double-click resets to the
  band-count default. Q is persisted with the channel and presets.
- The EQ response curve is now drawn from gain and Q together (a summed bell per
  band) instead of straight segments, so it reflects the bandwidth knobs.

### Changed
- The channel EQ pill is now a toggle: clicking it switches the EQ on/off. A new
  gear icon at the strip's right edge opens the EQ editor.
- The EQ editor opens docked just off the app's right edge (or left, if there is
  no room) instead of stacked on top; it remains a normal, movable window.
- EQ editor buttons, the preset picker and the name box now use the app's dark
  theme instead of default white Windows chrome.
- The EQ preset picker selects the built-in "Default" when the curve is flat or
  has just been reset, instead of showing a blank selection.

### Fixed
- The master strip's green "100%" reference line now sits on the fader's thumb
  centre at full scale (computed from the slider geometry) instead of floating
  above it.

## 0.2.9 - 2026-06-13

Master mute and a per-channel graphic equalizer.

### Added
- Master strip mute: the placeholder "ON" pill is now a real toggle (green "ON"
  while playing, red "MUTED" when engaged) bound to the source device's Windows
  mute, so it silences the whole mirrored master at once.
- Per-channel graphic EQ (`AudioHQ.Core/Equalizer.cs`): a bank of NAudio
  peaking-EQ biquad filters inserted between resampling and gain in each output
  pipeline. Selectable 3-band (100 / 1k / 8k Hz) or 6-band
  (80 / 200 / 500 / 1.2k / 3k / 8k Hz), +/-12 dB per band. Off by default
  (pass-through); reconfigured live and published atomically under a lock so a
  gain change never tears a filter mid-block.
- EQ editor window (`EqWindow`): enable toggle, 3/6-band switch and one vertical
  fader per band, spread evenly across the graph. A green 0 dB baseline aligned
  to the fader centres and a blue response curve drawn behind the faders that
  deforms live as they move. Double-click a fader (or Reset) returns it to 0 dB.
  Opened from each channel's EQ pill, which lights up blue while EQ is on.
- EQ state (`EqSettings`: enabled, band count, per-band gains) is persisted per
  channel in `settings.json` and reapplied on activation.
- EQ presets (`EqPresetStore`): name and save the current curve, then load it onto
  any channel from the editor's preset picker (Delete removes one). Presets are
  app-wide and persisted in `settings.json`; saving an existing name overwrites it.
  A built-in flat "Default" preset is always present and cannot be overwritten or
  deleted.

## 0.2.8 - 2026-06-11

Source-loss recovery - the app no longer goes silent when the capture source
disappears.

### Fixed
- When the source device was removed mid-session (e.g. unplugging a USB headset
  dongle), `WasapiLoopbackCapture` died but nothing noticed: the capture handle
  stayed non-null, so toggling an output OFF/ON just rebuilt a channel fed by the
  dead capture (buffer stuck at `fill 0,0ms`, no audio). The engine now detects
  this and recovers.

### Added
- `MirrorEngine`: subscribes to `RecordingStopped`, exposes `IsCapturing` and a
  `SourceLost` event raised on an unsolicited capture stop (intentional `Stop`
  detaches the handler first, so it is not mistaken for a loss).
- `MixerViewModel`: handles `SourceLost` (UI-thread marshaled) and runs a 3 s
  background watchdog (`DispatcherTimer`) that refreshes the device list and
  recovers when capture has died or the source device left the active list.
  Recovery re-resolves a live source (the saved one if it is back, else the
  default render device), restarts the engine, restores the channels that were
  ON, and reports the outcome via `EngineStatus` (`Source switched to 'X'.` on a
  fallback, cleared on a clean same-device recovery).
- Device hot-plug is now reflected live: the source picker and per-channel
  online/offline state update as devices come and go.

## 0.2.7 - 2026-06-11

### Changed
- Licensed under MIT (was "private, all rights reserved"). Added a `LICENSE`
  file and updated the README license section. Fully permissive: use, modify,
  distribute and sell, keep the copyright notice, no warranty.
- Removed local development instructions from the public repo, added them to
  `.gitignore`, and dropped the now-dead link from the README.

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
- Documentation package: README, repository conventions (versioning,
  file structure, git, language), docs/ARCHITECTURE.md, this changelog.
- `.gitignore` extended with .NET build artifacts (`bin/`, `obj/`, `.vs/`)
  and local tooling files.
