> Mirrored from microsoft/PowerToys PR 49571 for review iteration

## Summary of the Pull Request

Closes: PR 32206

Keyboard Manager's **Run program** action did not reliably honor `Elevation: Normal` when PowerToys was running as administrator. The existing path used `CreateProcessW` with Explorer as the parent process. That can create ordinary desktop executables with a non-elevated token, but it does not provide Explorer Shell activation semantics for packaged apps, app execution aliases, shortcuts, or file associations.

As a result, apps such as Windows Terminal, Firefox, VS Code, and other applications can start elevated, fail to connect to an existing non-elevated instance, use a different effective context, or fail to start at all. Users may then set mappings to `Elevated` merely to make them launch, unnecessarily granting administrator rights to the app and all child processes. This can affect profile/IPC behavior, drag and drop, updates, shell integration, and scripts launched by those applications.

This PR launches normal, visible program mappings through Explorer's `IShellDispatch2::ShellExecuteW` implementation. Explicit `Elevated` and `Different user` mappings retain their existing behavior. Hidden launches keep the `CreateProcessW` path because they need its window and process-ID control.

## PR Checklist

- [x] Closes: PR 32206
- [x] **Communication:** The issue discussion identifies this as unsafe/unexpected behavior and agrees it should be reworked
- [x] **Tests:** Added program-launch routing and working-directory tests; all Keyboard Manager Engine tests pass
- [x] **Localization:** No new user-facing strings
- [x] **Dev docs:** Not required for this behavioral bug fix
- [x] **New binaries:** None
- [x] **Documentation updated:** Not required

## Detailed Description of the Pull Request / Additional comments

- Routes normal, visible launches through the existing Explorer Shell automation helper so Shell targets receive the interactive user's non-elevated context.
- Keeps hidden launches on `CreateProcessW` to preserve hidden-window behavior and process tracking.
- Uses an executable's containing directory when `Start in` is empty, while leaving Shell targets such as `.lnk` files free to use their own metadata.
- Expands and validates an explicitly configured `Start in` directory before launching.
- Treats successful Shell dispatch as a successful launch even when no child PID is available.
- Checks `IShellDispatch2` and `ShellExecuteW` results instead of reporting success unconditionally.
- Closes process handles returned by elevated and different-user launches.
- Uses each matching PID when restoring an already-running application window.

No elevation checks, UAC behavior, code-signing checks, or Code Integrity policies are bypassed.

## Validation Steps Performed

- Built `KeyboardManagerEngineTest` with `tools\build\build.cmd -Platform x64 -Configuration Debug`: exit code 0, 0 warnings, 0 errors.
- Ran the native suite with Visual Studio `vstest.console.exe`: 109/109 tests passed.
- Built `KeyboardManagerEngine` and `PowerToys.KeyboardManager.dll`: exit code 0, 0 warnings, 0 errors.
- Ran `tools\build\build-essentials.cmd -Platform x64 -Configuration Debug`: exit code 0, 0 warnings, 0 errors.
- Manually ran PowerToys and Keyboard Manager Engine elevated, launched a mapping configured as `Normal`, and confirmed the target process was not elevated in Task Manager.
- Manually verified blank and explicit working directories, `.lnk` targets, file-association targets, and applications including Chrome, VS Code, Notion, and Steam.
- Confirmed mappings explicitly configured as `Elevated` still launch elevated.
