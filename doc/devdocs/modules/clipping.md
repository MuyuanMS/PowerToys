# ClipPing

ClipPing provides immediate visual confirmation when clipboard content changes. It listens for clipboard updates and briefly draws either a top-edge bar or a border around the active window.

## Architecture

ClipPing has two runtime components:

- `ClipPingModuleInterface` implements the PowerToys module interface. It applies the enabled-state policy, starts the WinUI process, and signals it through named events.
- `ClipPing` is the WinUI application. It listens for clipboard updates, reads the current settings, identifies the foreground window, and displays the overlay on the matching monitor.

Settings are stored in the standard PowerToys module settings file and exposed through `ClipPingPage` and `ClipPingViewModel`.

## Key files

- `src/modules/ClipPing/ClipPingModuleInterface/dllmain.cpp`: Runner integration and process lifecycle.
- `src/modules/ClipPing/ClipPing/ClipPingXAML/App.xaml.cs`: Clipboard listener, settings reload, and overlay placement.
- `src/settings-ui/Settings.UI.Library/ClipPingSettings.cs`: Serialized module settings.
- `src/settings-ui/Settings.UI/ViewModels/ClipPingViewModel.cs`: Settings persistence and policy state.
- `src/settings-ui/Settings.UI/SettingsXAML/Views/ClipPingPage.xaml`: Settings user interface.

## Build and test

Build the following projects for `x64 Debug`:

1. `ClipPingModuleInterface`
2. `ClipPing`
3. `Settings.UI`

Run the ClipPing tests in `Settings.UI.UnitTests`, including `ClipPingSettingsTests` and the `ViewModelTests.ClipPing` test class.

## Debugging

Build and start PowerToys, then enable ClipPing in Settings. Attach the managed debugger to `PowerToys.ClipPing.exe` to debug clipboard and overlay behavior. Attach the native debugger to `PowerToys.exe` to debug module startup, shutdown, policy enforcement, and named-event signaling.

The overlay should appear after copying content while another application window is active. Verify both overlay styles, custom colors, multiple monitors, display scaling, and enable/disable transitions.
