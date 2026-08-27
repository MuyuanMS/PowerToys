## Summary

Fixes the CmdPal Dock secondary monitor blank bar described in issue 49604.

Secondary monitor configs now inherit the global Dock band lists until the user explicitly customizes them. Existing legacy configs that were marked customized but only contained empty band lists are migrated back to global band inheritance during monitor reconciliation.

## Validation

- Built `Microsoft.CmdPal.UI.ViewModels.UnitTests.csproj` with Visual Studio MSBuild, Debug x64, using `/p:SpectreMitigation=false` because Spectre libraries are not installed locally.
- Ran `vstest.console.exe` for `FullyQualifiedName~DockMultiMonitorTests`: 38 passed, 0 failed.

## E2E verification steps

1. Launch a locally built PowerToys with CmdPal Dock enabled.
2. Use a two-monitor setup and enable the Dock on the secondary monitor.
3. Verify the secondary Dock shows the default Home, WinGet, Performance Monitor, and Date/Time bands instead of a blank bar.
4. Verify custom per-monitor band changes still persist after disconnecting and reconnecting a monitor.

## Confidence

High. The change directly fixes the approved root cause and adds regression coverage for both fresh secondary monitors and legacy empty-custom configs.
