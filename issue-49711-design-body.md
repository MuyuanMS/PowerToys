## Power Display tray icon missing after Windows startup

### Sanitized report

Issue 49711 reports that, on PowerToys 0.100.2 installed through GitHub auto-update, the Power Display notification-area icon is absent after starting the PC and appears after restarting PowerToys. The report contains no bug-report ZIP, logs, screenshots, hardware details, or independent reproduction.

### Evidence and inferred root cause

The current Power Display startup path creates the tray icon during `App.OnLaunched` in `src/modules/powerdisplay/PowerDisplay/PowerDisplayXAML/App.xaml.cs`. `TrayIconService.SetupTrayIcon` calls `Shell_NotifyIcon(NIM_ADD)` immediately. The code already acknowledges that Explorer may not be ready during system startup and clears `_trayIconData` when `NIM_ADD` fails.

The recovery path depends on a later `WM_WINDOWPOSCHANGING` message or `TaskbarCreated`. `TaskbarCreated` is not delivered when the initial registration failed, and there is no guaranteed contract that the hidden callback window receives a useful `WM_WINDOWPOSCHANGING` after Explorer becomes ready. Consequently, a transient startup failure can leave the icon absent until the process is restarted. This is the leading hypothesis, not a confirmed reproduction.

### Approved fix design

1. Keep the existing `TaskbarCreated` recovery for Explorer restarts.
2. Add a bounded, UI-thread retry path after an initial `NIM_ADD` failure (for example, a short DispatcherQueue timer with a small retry budget and increasing delay).
3. Stop the retry when registration succeeds, the icon is disabled, or the application shuts down. Do not spin or add logging in a tight loop.
4. Preserve the existing icon/menu/hook lifecycle and reset state consistently on failed registration.
5. Add unit-testable retry-state coverage in the Power Display library if the retry policy is extracted; manually verify the Win32 registration path on cold boot and Explorer restart.

### Repro and verification

**Current repro confidence:** low-to-medium. The only observed sequence is cold PC start → missing Power Display icon → restart PowerToys → icon appears.

To confirm the hypothesis, capture Power Display logs from a cold boot and verify an initial `Shell_NotifyIcon(NIM_ADD)` failure followed by no successful retry. Re-test with:

- Power Display enabled and system-tray icon enabled.
- Cold Windows startup with Explorer delayed or restarting.
- Icon visible after the bounded retry without restarting PowerToys.
- Explorer restart after a successful registration.
- Tray icon setting disabled and re-enabled.
- Clean shutdown and relaunch, ensuring no duplicate icons or leaked windows.

### Confidence and missing information

**Confidence:** medium for a startup-registration race; low for any hardware/display-specific explanation.

Missing evidence is the original cold-boot Power Display log, confirmation that the tray-icon setting was enabled, whether the icon was in the overflow area, and a second reproduction or system configuration. The design is ready for implementation as a narrow resilience fix, but the report is not sufficient to claim the root cause is proven.

### Adversary review

- A disabled tray-icon setting or overflow placement could explain the symptom; the verification matrix explicitly separates those cases.
- A process-startup failure would not be fixed by retrying `NIM_ADD`; cold-boot logs must confirm the process reached tray setup.
- Retry must be bounded and state-safe so it cannot create duplicate icons or busy-loop while Explorer is unavailable.
- Settings changes and shutdown must cancel or supersede any pending retry, and a successful registration must make subsequent retries no-ops.
- No upstream comments, labels, cross-references, or pull requests are part of this mirror.

**Adversary sign-off:** no design blocker remains; the retry is narrow, bounded, and preserves the existing Explorer-restart path.

**Design status:** approved for implementation after capturing at least one cold-boot log, or as a low-risk defensive fix if reproducing the startup race is impractical.
