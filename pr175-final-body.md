## What was fixed

Always On Top settings writes now notify the running daemon after `set_config()` saves settings, using the existing registered settings-changed window message. This triggers the existing live reload path for hotkeys, frame, border, excluded apps, sound, and system-menu settings.

The shared `FileWatcher` was also hardened so the first observed creation/write transition is treated as a change instead of being silently recorded without invoking the callback.

Plain reference: Issue 49599.

## How to verify

Use the local build worktree at `C:\ptbuild-175`:

1. Launch `C:\ptbuild-175\x64\Debug\PowerToys.exe`.
2. Enable Always On Top and pin a test window.
3. Change Always On Top settings from Settings and verify each takes effect immediately without restarting PowerToys:
   - sound enabled/disabled,
   - system-menu integration,
   - frame visibility,
   - border color, thickness, opacity, and corners,
   - excluded apps,
   - activation hotkey re-registration.

## Confidence

High. The PR implements the approved design directly by broadcasting the existing settings-changed message and preserving the existing reload/observer logic.

## Reviewer and build summary

- Copilot review rounds: 2.
- Round 1 produced two actionable comments; both were fixed, replied to, and resolved.
- Round 2 produced no new inline comments and no unresolved Copilot review threads.
- Local validation: `SettingsAPI.vcxproj`, `AlwaysOnTop.vcxproj`, and `AlwaysOnTopModuleInterface.vcxproj` built successfully for x64 Debug in `C:\ptbuild-175`.
- `PowerToys.exe` and `PowerToys.AlwaysOnTop.exe` exist under `C:\ptbuild-175\x64\Debug`.

## Known limitations

No Always On Top unit-test project was found for this behavior. Manual e2e verification is recommended using the steps above.

A full `build-essentials.cmd` run restored and built the runner, then stopped in unrelated ZoomIt code with codepage warning C4819 treated as an error on this machine. The targeted changed projects built cleanly afterward.
