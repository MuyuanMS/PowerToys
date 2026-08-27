> Mirrored from microsoft/PowerToys PR 49412 for review iteration. References below are de-linked (plain text) to avoid cross-notifications.

## Summary

Adds audible feedback to PowerToys Run: a sound plays when the Run window opens and/or closes. Replaces an earlier system-sound dropdown design with a simpler toggle-based UI and two custom, purpose-recorded `.wav` files instead of relying on Windows system sounds.

- New Settings UI: a single "Audible feedback" toggle, with two dependent sub-toggles ("Opening Sound" / "Closing Sound") that are disabled unless the parent toggle is on.
- Two new bundled audio assets (`open.wav`, `close.wav`).
- Playback uses `winmm.dll`'s `PlaySound` API, invoked from `MainWindow.xaml.cs` on `OnVisibilityChanged`.

Closes Issue 11225 (Audible Feedback for PowerToys Run). Parent PR 48911.

## Details

**Settings model (`PowerLauncherProperties.cs`):** Added three new boolean properties `EnableAudibleFeedback`, `EnableOpeningSound`, `EnableClosingSound`, each defaulting to `false`.

**ViewModel (`PowerLauncherViewModel.cs`):** Added matching wrapper properties that propagate changes and trigger `UpdateSettings()`/IPC sync.

**Settings UI (`Resources.resw` + XAML):** `SettingsExpander` containing a top-level toggle and two dependent `SettingsCard` toggles, each `IsEnabled`-bound to the parent toggle.

**Runtime playback (`MainWindow.xaml.cs`):** `PlaySound` P/Invoke against `winmm.dll`. `PlayAudibleFeedback(bool isOpening)` checks toggles and plays `open.wav`/`close.wav` from a `Sounds/` folder next to the executable, guarded by a `File.Exists` check. A `_loadedAtLeastOnce` guard avoids firing the opening sound on cold start.

**Audio assets:** `Sounds/open.wav`, `Sounds/close.wav` added to `PowerLauncher.csproj` with `CopyToOutputDirectory = PreserveNewest`. Third-party licensing to be confirmed before upstream merge.

**Tests (`Settings.UI.UnitTests`):** New toggle-propagation test, backward-compat defaults test, and `--settings` CLI DataRows.

## Note for review iteration

Upstream Azure CI Release build (x64 + arm64) is reported failing. Reproducing and diagnosing that build failure is a first-class goal of this review pass.
