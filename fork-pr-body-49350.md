> Mirrored from microsoft/PowerToys PR 49350 for review iteration.

## Summary
- Adds a `ThreeMfThumbnailProvider` (C# + C++ COM host) so Windows File Explorer can show thumbnails for `.3mf` files, matching the existing STL thumbnail architecture.
- Loads embedded package thumbnails from the 3MF ZIP when present; otherwise parses mesh data with `System.IO.Compression` / `System.Xml.Linq` and renders via HelixToolkit (no new NuGet dependencies).
- Wires settings toggle + color picker, GPO policy (`SUPPORTED_POWERTOYS_0_100_0`), registry registration, installer process list, signing, Bug Report Tool, DSC examples, and unit tests.
- Updates OOBE File Explorer copy so 3MF is described as thumbnail support only (not preview pane).

## Product / design notes for reviewers
- **Why not rely on Windows / 3D Viewer alone?** Prior issues (Issue 22064, Issue 31498) were closed pointing to 3D Viewer. That path often requires the Store app and/or the correct default association. This PR mirrors STL: PowerToys can provide Explorer thumbnails even when 3D Viewer is not installed or not the default handler, with a configurable material color.
- **Handler registration:** When the toggle is On, PowerToys registers `.3mf\shellex\{E357FCCD-A995-4576-B01F-234630154E96}` to the new CLSID (same pattern as STL/PDF/SVG). That can override a built-in `ms3dthumbnailprovider` association while the feature is enabled. Turning the setting Off removes the PowerToys registration.
- **STEP (`.step` / `.stp`) is intentionally out of scope.** STEP needs a CAD kernel (e.g. Open CASCADE) to tessellate B-rep geometry.
- Thumbnail-only (same as STL). Preview pane for 3MF remains available via Microsoft 3D Viewer when installed.
- Related requests: Issue 22064, Issue 31498, Issue 27749.

## Test plan
- [ ] Build `ThreeMfThumbnailProvider`, `ThreeMfThumbnailProviderCpp`, and `Preview.ThreeMfThumbnailProvider.UnitTests` (x64 Release)
- [ ] Run `Preview.ThreeMfThumbnailProvider.UnitTests` (valid stream / invalid size / empty / null)
- [ ] Enable **File Explorer add-ons > 3D Manufacturing Format** in Settings
- [ ] Verify thumbnails appear for `.3mf` files that contain an embedded package thumbnail
- [ ] Verify thumbnails appear for mesh-only `.3mf` files (no embedded thumbnail), and that the color setting applies
- [ ] With the toggle On, confirm PowerToys owns the `.3mf` thumbnail shellex key; with it Off, confirm registration is removed
- [ ] Confirm GPO `ConfigureEnabledUtilityFileExplorerThreeMfThumbnails` disables the provider
- [ ] Confirm OOBE / overview text does not claim 3MF preview-pane support
