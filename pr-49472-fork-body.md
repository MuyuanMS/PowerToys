> Mirrored from microsoft/PowerToys PR 49472 for review iteration

## Summary of the Pull Request

Adds **DemoMirror** to ZoomIt: a new hotkey family (default Ctrl+9) that live-mirrors content onto a second monitor — designed for presenters who want to demo an app on their laptop screen while the audience sees it on the presentation display (e.g., on top of a PowerPoint slideshow), without leaving Presenter View.

- **Ctrl+9** — mirror the entire monitor under the cursor
- **Ctrl+Shift+9** — mirror a selected region
- **Ctrl+Alt+9** — mirror the window under the cursor (with optional window tracking)

The mirror includes the mouse cursor, targets the monitor running a PowerPoint slideshow when one is found (otherwise the first non-source monitor), letterboxes on a black backdrop, shows a bright green border around the mirrored source, and works with ZoomIt's existing zoom and draw/annotation modes. The mirrored content is visible in Teams/meeting screen shares.

## PR Checklist

- [ ] Closes: PR xxx
- [ ] **Communication:** I've discussed this with core contributors already. If the work hasn't been agreed, this work might be rejected
- [ ] **Tests:** Added/updated and all pass
- [x] **Localization:** All end-user-facing strings can be localized
- [ ] **Dev docs:** Added/updated
- [x] **New binaries:** None added
- [ ] **Documentation updated:** If checked, please file a pull request on [our docs repo](https://github.com/MicrosoftDocs/windows-uwp/tree/docs/hub/powertoys) and link it here: PR xxx

## Detailed Description of the Pull Request / Additional comments

**Capture & rendering:** Uses Windows.Graphics.Capture through the existing `CaptureFrameWait` helper (cursor capture enabled, system capture border suppressed). A new `MirrorWindow` class hosts a topmost, no-activate, click-through window on the target monitor with an HWND-bound flip-model swapchain; a render thread caches frames so static content keeps rendering. A black backdrop window letterboxes the mirrored content.

**Window mode:** Windows mirror at native size (scaled down only if larger than the target monitor), crop to the DWM extended frame bounds (`DWMWA_EXTENDED_FRAME_BOUNDS`) to exclude invisible resize borders, adapt to source-window resizes by recreating the frame pool and swapchain, and auto-stop when the mirrored window closes. A new **Track window** setting (default on) captures the monitor cropped to the tracked window each frame so annotations show in place and the crop follows moves/resizes; when off, window-surface capture is used instead.

**Annotations:** Window capture can't see ZoomIt's own overlay windows, so while zoom/draw/LiveZoom is active the render thread switches capture to the monitor under the cursor and back on exit, letting annotations appear in the mirror.

**Screen-share compatibility:** The mirror and backdrop windows are visible to display capture so remote meeting attendees see the mirrored content; self-capture feedback loops are avoided by substituting the source monitor when the annotation-override capture would resolve to the target monitor. The green border stays above ZoomIt's own fullscreen-topmost windows via the mirror's topmost-reassert timer.

**Win11 capture border:** `IsBorderRequired(false)` is silently ignored until the process calls `GraphicsCaptureAccess::RequestAccessAsync(Borderless)`; this is now requested (auto-granted for desktop apps) before session creation.

**Settings:** New DemoMirror tab in ZoomIt's options dialog (hotkey + track-window checkbox), persisted in registry settings, and exposed in the PowerToys Settings UI (`ZoomItProperties`, `ZoomItViewModel`, `ZoomItPage.xaml`) with localizable strings in `Resources.resw`.

## Validation Steps Performed

Manually tested on a two-monitor setup (including a Surface ARM device with an external monitor):

- Monitor, region, and window mirroring via all three hotkeys, with the mouse cursor visible in the mirror
- Native-size and scaled-down window mirroring; letterboxing on the backdrop
- Window move/resize while mirrored (both tracking and non-tracking modes); auto-stop on window close
- Zoom and draw annotations appearing in the mirror in all modes
- Mirrored content visible to remote attendees in a real Microsoft Teams display share
- Green border correctly colored and staying above static zoom; no lingering Windows 11 capture border
- Settings round-trip through both the ZoomIt options dialog and the PowerToys Settings UI

🤖 Generated with [Claude Code](https://claude.com/claude-code)