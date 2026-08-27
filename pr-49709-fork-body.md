> Mirrored from microsoft/PowerToys PR 49709 for review iteration

## Summary
Makes Settings search-index generation incremental across Debug and Release builds, avoiding repeated generation when XAML and generator inputs are unchanged while preserving a valid embedded index on clean builds.

## Validation
Built the PowerToys Debug essentials and the Settings UI and XAML index builder projects locally.

## Scope
- `src/settings-ui/Settings.UI.XamlIndexBuilder/Settings.UI.XamlIndexBuilder.csproj`
- `src/settings-ui/Settings.UI/PowerToys.Settings.csproj`

This fork PR is for private review iteration only.