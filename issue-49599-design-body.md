## Approved fix design for Issue 49599

### Problem
Always On Top settings changes currently appear to require restarting PowerToys before they affect the running Always On Top daemon.

### Inferred root cause
`src\modules\AlwaysOnTop\AlwaysOnTopModuleInterface\dllmain.cpp` updates settings in `set_config()` by parsing hotkeys and calling `save_to_settings_file()`, but it does not notify the running Always On Top daemon after the write.

The daemon already has a live reload path: `AlwaysOnTopSettings::LoadSettings` notifies observers, `AlwaysOnTop::SettingsUpdate` re-applies hotkeys, frame, excluded apps, and system menu behavior, and `WindowBorder::SettingsUpdate` re-applies border color, thickness, opacity, and corner settings. That path currently depends on the file watcher, whose first detected write can be suppressed when its `m_lastWrite` value is empty.

### Fix plan
After `values.save_to_settings_file()` in `AlwaysOnTopModuleInterface\dllmain.cpp` `set_config()`, broadcast the existing registered Always On Top settings-changed window message GUID used by `WinHookEventIDs.cpp`, so the running daemon re-reads the config and applies the existing live reload path. Keep existing reload logic intact.

Optional low-risk hardening: fix the file watcher first-write suppression only if it is straightforward and tightly scoped.

Do not add restart requirements. The affected Always On Top settings are expected to be hot-applied.

### How to verify
Manually verify these settings take effect immediately without restarting PowerToys:

- Toggle sound.
- Toggle system menu integration.
- Toggle frame visibility.
- Change border color, thickness, opacity, and corners.
- Change excluded apps.
- Change activation hotkey and verify it still re-registers through the runner/settings path.

No existing Always On Top unit-test project was found in the approved design. Add tests only if there is a nearby maintained test target that can cover this behavior without creating unrelated infrastructure.

### Adversary sign-off
High confidence. The proposed change uses the existing daemon reload mechanism and avoids redesigning Always On Top settings handling.
