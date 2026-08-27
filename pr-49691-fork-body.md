> Mirrored from microsoft/PowerToys PR 49691 for review iteration

## Summary
- replace the misleading Gliding Cursor description in PowerToys Settings
- clarify that the feature positions the cursor and clicks using only a keyboard shortcut

## Validation
- parsed `Resources.resw` as XML
- `git diff --check`
- Settings UI dependency restore completed; the build could not finish because the D: drive ran out of space

Addresses issue 45598.
