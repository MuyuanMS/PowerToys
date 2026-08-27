## Approved fix design for issue 49604

Title: CmdPal Dock blank on secondary monitor

## Root cause

MonitorConfigReconciler creates secondary-monitor configs as customized while also giving them empty band lists. DockSettings then resolves those empty customized lists instead of falling back to the global default bands, so the secondary Dock renders as a blank bar with content indented down.

## Fix plan

1. Create new secondary-monitor configs as uncustomized so their bands resolve from global defaults.
2. During monitor reconciliation, migrate existing configs that are customized but have empty start, center, and end band lists back to uncustomized inheritance.
3. Preserve real per-monitor customizations when any custom band list contains entries.
4. Update DockMultiMonitorTests.cs to cover fresh secondary monitors resolving default bands and legacy empty-custom configs migrating and resolving defaults.

## Verify steps

- Run the CmdPal Dock multi-monitor unit tests, especially the fresh secondary-monitor and legacy empty-custom migration tests.
- Manually verify on a multi-monitor setup that enabling the Dock on a secondary monitor shows the default bands rather than a blank bar.

## Approval

Approved design supplied for implementation. Keep fork artifacts safe: refer to plain issue 49604 only, with no upstream cross-reference syntax.
