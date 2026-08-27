> Mirrored from microsoft/PowerToys PR 49301 for review iteration

Render the WebView2 preview on a transparent background and keep the alpha channel when resizing, so SVGs with transparency no longer render as black thumbnails.

This also ensures we return an ARGB bitmap that matches what is expected in StlThumbnailProvider.cpp with `WTS_ALPHATYPE::WTSAT_ARGB`.

## Summary of the Pull Request

Preserve alpha transparency for SVG thumbnails in File Explorer.

## PR Checklist

- Closes: PR 36234
- Communication: discussed with core contributors as needed
- Tests: no automated tests added; manual screenshots included upstream
- Localization: no end-user-facing strings changed
- Dev docs: not applicable
- New binaries: not applicable

## Screenshots

Before and after screenshots are included in the original pull request.

## Validation Steps Performed

Manual validation was documented with before/after screenshots.
