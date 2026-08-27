> Mirrored from microsoft/PowerToys PR 49662 for review iteration

## Summary
Fix abnormal blinking in ShortcutControl when users hold a non-modifier key or the Windows key while creating a new hotkey shortcut.

## Validation
Try creating a shortcut and keep the last key pressed for several seconds.

## Original context
The latest release of PowerToys still blinks abnormally when keeping non-modifier keys or the Windows key pressed. This change addresses that behavior.

Tests are marked as added/updated and passing; no localization or developer documentation changes are required.
