> Mirrored from microsoft/PowerToys PR 49427 for review iteration

## Summary of the Pull Request

Adds a new DSC v3 resource, `profile` (`Microsoft.PowerToys/KeyboardManagerProfile`), to `PowerToys.DSC.exe` that makes Keyboard Manager key and shortcut remappings deployable via `dsc.exe` and `winget configure`. Until now, DSC only controlled whether Keyboard Manager is enabled — the actual remappings could only be created through the Keyboard Manager editor UI.

Remappings are authored with friendly, layout-invariant key names instead of raw virtual-key codes:

```yaml
resources:
  - name: Deploy key remappings
    type: Microsoft.PowerToys/KeyboardManagerProfile
    properties:
      profile:
        keys:
          - { from: CapsLock, to: Esc }
          - { from: Insert, to: Disable }
        shortcuts:
          - { from: "Ctrl+Shift+A", to: "Ctrl+V" }
          - { from: "Win+O, K", toText: "chord-triggered text" }
          - { from: "Ctrl+Alt+N", to: "Ctrl+S", targetApp: "notepad.exe", exactMatch: true }
```

The resource supports `get`/`set`/`test`/`export`/`schema`/`manifest`, applies replace-whole-profile semantics (declarative desired state), writes the exact profile encoding the C++ editor produces, and signals a running Keyboard Manager engine to reload the remappings immediately, meaning no PowerToys restart is required.

## PR Checklist

- Closes: Issue 38233
- Closes: Issue 4452 (export/import of remapping configurations is covered by `export` + `set`)
- Tests: Added/updated and all pass
- Localization: All end-user-facing strings can be localized (new strings added to `PowerToys.DSC` `Resources.resx`; generated DSC manifest descriptions are deliberately not localized, matching the existing `settings` resource)
- Dev docs: Added/updated (`doc/dsc/profile-resource.md`, `doc/dsc/overview.md`, `doc/dsc/modules/KeyboardManager.md`, `doc/devdocs/core/settings/dsc-configure.md`)
- New binaries: n/a — no new binaries; the resource lives in the existing `PowerToys.DSC.exe`. The additional generated manifest (`microsoft.powertoys.KeyboardManager.profile.dsc.resource.json`) is picked up automatically by the installer's unfiltered `DSCModules\` component glob (`generateAllFileComponents.ps1`)

## Detailed Description of the Pull Request / Additional comments

As remarked on the issue, remappings don't live in `settings.json`. They're stored in a separate profile file: `%LOCALAPPDATA%\Microsoft\PowerToys\Keyboard Manager\<activeConfiguration>.json`. The only KBM properties that reference it in `settings.json` are marked `[CmdConfigureIgnore]` (`activeConfiguration`, `keyboardConfigurations`).

The legacy PowerShell-based `PowerToysConfigure` (v2/winget 0.2 schema) path was deliberately not chosen. It drives settings through a scalar `PowerToys.Settings.exe set <Module>.<Property> <value>` protocol; nested lists only work through the special-cased `setAdditional` side-channel, which merges by a `Name` key into the module's `settings.json` and cannot target the separate profile file. Its `Set()` also would have killed, requiring a restart for PowerToys.

The style is also in friendly strings and not in `HotkeySettings`-style, as the shape couldn't express what remappings it needed.
