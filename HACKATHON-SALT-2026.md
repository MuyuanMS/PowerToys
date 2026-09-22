# PowerToys Hackathon SALT 2026 demo branch

This branch combines four experimental projects for the September 2026 booth:

- **ScreenSlice / ZoomIt virtual region mirror** from `yuleng/zoomit-virtual-region-mirror`
- **Screen Translator** from `muyuanli/screen-translator-acp-experiment`
- **PowerToys Capsule / Try Run** from `user/shawn/mxc-hackathon`
- **PowerScripts** from `muyuanli/powerscripts`

The integration branch is `muyuanli/hackathonsalt2026`.

## Automated dependency setup

Prerequisites:

- Windows x64 or ARM64.
- Git.
- For `-Build`: Visual Studio with the PowerToys native and managed build prerequisites, plus the .NET 10 SDK.

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\prepare-hackathon-salt-2026.ps1 -InstallRust
```

The script:

1. Installs/selects Rust 1.93 through `rustup`.
2. Clones `shuaiyuanxx/mxc` branch `codex/try-run-policy-bridge` and checks out the required commit `3eef7d60ce35d4d0ba568ddd0a9108beadb35b9a`.

It does **not** build PowerToys by default. To run the targeted Debug build, specify the platform explicitly:

```powershell
.\tools\build\prepare-hackathon-salt-2026.ps1 -Build -Platform x64
.\tools\build\prepare-hackathon-salt-2026.ps1 -Build -Platform ARM64
```

The build option restores the isolated PowerScripts projects and builds only the affected projects, Runner, and Settings. Add `-IncludeWslc` to build Try Run with its optional WSLC image helper. WSL 2.9.9 or newer must already be installed and compatible; the script does not enable Windows features or update WSL.

The experimental ZoomIt virtual-display driver is currently **x64-only**. On an x64 machine, prepare and stage it with:

```powershell
.\tools\build\prepare-hackathon-salt-2026.ps1 -PrepareZoomItDriver -StageZoomItDriver
```

This requests one UAC approval to stage the signed driver INF. It does not create a display device or change Windows security settings.

After closing other PowerToys instances, the script can launch an existing Debug build:

```powershell
.\tools\build\prepare-hackathon-salt-2026.ps1 -LaunchPowerToys -Platform x64
```

MXC is stored by default under:

```text
%LOCALAPPDATA%\PowerToysHackathonDependencies\mxc
```

Use `-MxcRoot C:\mxc` to use or create another checkout. The current repository build wrapper forwards MSBuild properties as space-separated arguments, so avoid spaces in this path.

## Manual build commands

If the setup script is unavailable, prepare the exact MXC dependency:

```powershell
git clone --branch codex/try-run-policy-bridge https://github.com/shuaiyuanxx/mxc.git C:\mxc
git -C C:\mxc checkout 3eef7d60ce35d4d0ba568ddd0a9108beadb35b9a
git -C C:\mxc submodule update --init --recursive
rustup toolchain install 1.93.0 --profile minimal
$env:PATH = "$env:USERPROFILE\.cargo\bin;$env:PATH"
$env:RUSTUP_TOOLCHAIN = "1.93.0"
```

Prepare the ZoomIt driver:

```powershell
.\src\modules\ZoomIt\scripts\Prepare-VirtualDisplayDriver.ps1 -Stage
```

Build Try Run with the pinned checkout:

```powershell
.\tools\build\build.ps1 -Platform x64 -Configuration Debug -Path .\src\modules\TryRun /p:MxcRoot=C:\mxc
```

The remaining affected projects are listed in `tools\build\prepare-hackathon-salt-2026.ps1`.

## Booth testing

The booth build validated for this branch is x64 Debug. The script forwards `-Platform ARM64` to the targeted PowerToys builds, but that combination has not been booth-validated. The ZoomIt virtual-display-driver experiment is unavailable on ARM64.

### ZoomIt virtual region mirror

1. Exit other ZoomIt instances or disable ZoomIt in the integrated PowerToys.
2. Run `x64\Debug\PowerToys.ZoomIt.exe` as administrator.
3. Accept the first-run license and close Options.
4. Press **Ctrl+Shift+9** using the main keyboard number row, then select a region on one physical display.
5. Confirm a temporary second display appears in **Settings > System > Display**.
6. Select that display in the meeting application's sharing picker.
7. Press **Ctrl+Shift+9** again to stop and confirm that the temporary display disappears.

Do not copy `vdd_settings.xml` to `C:\VirtualDisplayDriver`. An existing VDD device or shared VDD configuration can cause this proof of concept to refuse startup.

Other ZoomIt mirror shortcuts:

| Shortcut | Action |
| --- | --- |
| `Ctrl+9` | Mirror the full screen to an existing second display |
| `Ctrl+Alt+9` | Mirror a selected window to an existing second display |
| `Escape` | Cancel region selection |

### Screen Translator

Launch `x64\Debug\PowerToys.exe`, enable Screen Translator in Settings, and use its configured capture/translation shortcuts. The default scan-text shortcut is **Win+Alt+Shift+T**.

### Try Run / PowerToys Capsule

Try Run requires Windows 11 24H2 or later for the tested ProcessContainer path. It should run as a normal user, not from an elevated PowerToys session. The application and worker outputs are under:

```text
x64\Debug\TryRun
```

### PowerScripts

PowerScripts is an optional prototype integrated with Settings, Keyboard Manager, Advanced Paste, and Command Palette. Its isolated projects restore from `src\modules\PowerScripts\nuget.config`.

## Copilot recovery prompt

If setup fails, run Copilot CLI from the repository root with:

> Read `HACKATHON-SALT-2026.md`, inspect the failing build log, and finish preparing the x64 Debug booth build. Preserve the pinned MXC revision and do not replace it with public MXC main. Prioritize any UAC or other interactive steps before long builds.
