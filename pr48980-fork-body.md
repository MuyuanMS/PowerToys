> Mirrored from microsoft/PowerToys PR 48980 for review iteration

## Summary of the Pull Request

Adds a small, native progress/result window to the **Bug Report** flow so users get feedback while the report is generated and a one-click path to file a GitHub issue.

Previously, triggering "Report bug" (from the tray menu or **Settings → General**) ran `PowerToys.BugReportTool.exe` hidden for ~30 seconds with **no feedback at all**, then popped a plain message box. Many users then had to manually find the `.zip` and figure out where to file the issue.

Now the runner shows a lightweight window that:

- Displays an animated **"Generating bug report…"** state while the tool runs.
- On completion, shows **where the `.zip` was saved** (`…\Desktop\PowerToysReport_<timestamp>.zip`) in a read-only, copyable field.
- Offers **Open folder** (reveals/selects the `.zip` in Explorer) and **Report on GitHub** (opens the prefilled `bug_report.yml` issue template *and* reveals the `.zip` so it can be dragged into the issue).
- Shows a clear error state if the report could not be created.

> Note: GitHub has no API/URL to pre-attach a binary to a new issue (attachments only happen via browser drag-drop). So the "Report on GitHub" action does the next best thing: opens the prefilled issue page and highlights the `.zip` in Explorer for a single drag to attach.

https://github.com/user-attachments/assets/9307d728-bbbd-4258-9480-ced65d2fa065


## PR Checklist

- [ ] Closes: #xxx
- [x] **Communication:** Lightweight, additive UX on an existing feature; happy to adjust per maintainer feedback.
- [ ] **Tests:** No automated tests (native Win32 window in the runner); validated manually — see below.
- [x] **Localization:** All end-user-facing strings are added to `src/runner/Resources.resx` and loaded via `GET_RESOURCE_STRING`.
- [ ] **Dev docs:** N/A
- [x] **New binaries:** None — `bug_report_dialog.cpp/.h` compile into the existing `PowerToys.exe` (runner). No new WinUI app or DLL, so no signing/WXS/CI changes required.

## Detailed Description of the Pull Request / Additional comments

- New files `src/runner/bug_report_dialog.{h,cpp}` implement the window as plain Win32 (no Common Controls v6 dependency, no managed/WinUI payload), so it works for **both** entry points since it lives in the runner.
- `bug_report.cpp` now calls `run_bug_report_dialog(...)` instead of the silent run + message box. The "running" state (observed by Settings) is cleared as soon as the **tool process** exits, so the result window can stay open without keeping the Settings button spinning. A guard re-focuses an already-open window instead of starting a second report.
- The window uses the canonical `AttachThreadInput` foreground recipe so it reliably surfaces even when launched from Settings (a different foreground process), and gets a taskbar button so it stays findable during the ~30s run.
- The output path is discovered by locating the newest `PowerToysReport_*.zip` in the Desktop folder after the tool exits (the tool names the file internally with a timestamp).
- Strings added: dialog title, generating/hint text, done header/hint, failed text, and button captions.

## Validation Steps Performed

- Triggered **Report bug** from the **system tray** menu: window appears in the foreground, animates "Generating…", then shows the saved `.zip` path with working **Open folder** and **Report on GitHub** buttons.
- Verified **Open folder** selects the `.zip` in Explorer and **Report on GitHub** opens the prefilled `bug_report.yml` issue template with the `.zip` highlighted for drag-and-drop.
- Verified the error state renders correctly (and wraps long localized text) when the tool can't run.
- Built `runner` (ARM64, Debug) clean; verified end-to-end on a high-DPI display.

