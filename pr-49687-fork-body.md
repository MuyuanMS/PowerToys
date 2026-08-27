> Mirrored from microsoft/PowerToys PR 49687 for review iteration.

## Summary

Adds translator guidance for the Mouse Without Borders OOBE description so the German translation uses a literal ampersand in "Drag & Drop" instead of displaying the HTML entity text.

Linked issue: PowerToys issue 42943.

## Validation from the original PR

- Parsed `Resources.resw` as XML and confirmed the comment resolves to the intended literal ampersand and not the entity text.
- Confirmed the patch passes `git diff --check`.
