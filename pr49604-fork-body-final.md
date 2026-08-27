## Summary

Fixes the CmdPal Dock secondary monitor blank bar described in issue 49604.

New secondary monitor configs now inherit global Dock band lists until the user explicitly customizes that monitor. Existing legacy secondary configs that were automatically created as customized with three empty band lists are migrated back to global band inheritance. Explicit user customizations, including intentionally empty band lists, are preserved with a persisted marker and regression coverage.

## Validation

- Built `Microsoft.CmdPal.UI.ViewModels.UnitTests.csproj` with Visual Studio MSBuild, Debug x64, using `/p:SpectreMitigation=false` because Spectre libraries are not installed locally.
- Ran `vstest.console.exe` for `FullyQualifiedName~DockMultiMonitorTests`: 42 passed, 0 failed.
- Copilot code review loop: clean fresh review after 8 requested rounds; 0 unresolved Copilot threads.

## E2E verification steps

1. Launch a locally built PowerToys with CmdPal Dock enabled.
2. Use a two-monitor setup and enable the Dock on the secondary monitor.
3. Verify the secondary Dock shows the default Home, WinGet, Performance Monitor, and Date/Time bands instead of a blank bar.
4. Verify custom per-monitor band changes still persist after disconnecting and reconnecting a monitor.

## Confidence

High. The change directly fixes the approved root cause and includes regression coverage for fresh secondary monitors, legacy empty-custom secondary configs, explicit empty customizations, JSON persistence, and primary-role edge cases.

## Known limitations

Full application e2e on physical multi-monitor hardware was not run in this environment.
