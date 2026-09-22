# Copyright (c) Microsoft Corporation
# The Microsoft Corporation licenses this file to you under the MIT license.

<#
.SYNOPSIS
Prepares dependencies for the PowerToys Hackathon SALT 2026 integration branch.

.DESCRIPTION
Pins Shawn's MXC fork and Rust toolchain, prepares the ZoomIt virtual-display
driver when requested, and can optionally build the affected Debug projects.
Use -Build to compile, -StageZoomItDriver to request the required UAC prompt,
and -LaunchPowerToys to start an existing build.
#>
[CmdletBinding()]
param(
    [string] $MxcRoot = (Join-Path $env:LOCALAPPDATA 'PowerToysHackathonDependencies\mxc'),
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',
    [switch] $InstallRust,
    [switch] $PrepareZoomItDriver,
    [switch] $StageZoomItDriver,
    [switch] $IncludeWslc,
    [switch] $Build,
    [switch] $LaunchPowerToys
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$buildScript = Join-Path $PSScriptRoot 'build.ps1'
$driverScript = Join-Path $repoRoot 'src\modules\ZoomIt\scripts\Prepare-VirtualDisplayDriver.ps1'
$powerScriptsRoot = Join-Path $repoRoot 'src\modules\PowerScripts'
$mxcRemote = 'https://github.com/shuaiyuanxx/mxc.git'
$mxcBranch = 'codex/try-run-policy-bridge'
$mxcRevision = '3eef7d60ce35d4d0ba568ddd0a9108beadb35b9a'
$rustToolchain = '1.93.0'

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(ValueFromRemainingArguments)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE."
    }
}

function Assert-Command {
    param([Parameter(Mandatory)][string] $Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found."
    }
}

function Initialize-Rust {
    $cargoBin = Join-Path $env:USERPROFILE '.cargo\bin'
    if (Test-Path $cargoBin) {
        $env:PATH = "$cargoBin;$env:PATH"
    }

    if (-not (Get-Command rustup -ErrorAction SilentlyContinue)) {
        if (-not $InstallRust) {
            throw "rustup is required. Re-run with -InstallRust or install it from https://rustup.rs/."
        }

        $rustTarget = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            'aarch64-pc-windows-msvc'
        } else {
            'x86_64-pc-windows-msvc'
        }
        $rustupInstaller = Join-Path $env:TEMP 'rustup-init.exe'
        $rustupUri = "https://static.rust-lang.org/rustup/dist/$rustTarget/rustup-init.exe"
        Invoke-WebRequest -Uri $rustupUri -OutFile $rustupInstaller -UseBasicParsing
        Invoke-NativeCommand $rustupInstaller '-y' '--profile' 'minimal' '--default-toolchain' $rustToolchain
        $env:PATH = "$cargoBin;$env:PATH"
    }

    Invoke-NativeCommand rustup 'toolchain' 'install' $rustToolchain '--profile' 'minimal'
    $env:RUSTUP_TOOLCHAIN = $rustToolchain
    Invoke-NativeCommand rustc '--version'
}

function Initialize-Mxc {
    $mxcParent = Split-Path $MxcRoot -Parent
    New-Item -ItemType Directory -Path $mxcParent -Force | Out-Null

    if (-not (Test-Path (Join-Path $MxcRoot '.git'))) {
        Invoke-NativeCommand git 'clone' '--filter=blob:none' '--branch' $mxcBranch $mxcRemote $MxcRoot
    } else {
        $dirty = & git -C $MxcRoot status --porcelain
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect the existing MXC checkout at $MxcRoot."
        }
        if ($dirty) {
            throw "The existing MXC checkout at $MxcRoot has local changes. Clean or move it before rerunning."
        }

        $remoteUrl = (& git -C $MxcRoot remote get-url hackathon 2>$null)
        if ($LASTEXITCODE -ne 0) {
            Invoke-NativeCommand git '-C' $MxcRoot 'remote' 'add' 'hackathon' $mxcRemote
        } elseif ($remoteUrl -ne $mxcRemote) {
            Invoke-NativeCommand git '-C' $MxcRoot 'remote' 'set-url' 'hackathon' $mxcRemote
        }

        Invoke-NativeCommand git '-C' $MxcRoot 'fetch' 'hackathon' $mxcBranch
    }

    Invoke-NativeCommand git '-C' $MxcRoot 'checkout' '--detach' $mxcRevision
    Invoke-NativeCommand git '-C' $MxcRoot 'submodule' 'update' '--init' '--recursive'

    $actualRevision = (& git -C $MxcRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualRevision -ne $mxcRevision) {
        throw "MXC revision mismatch. Expected $mxcRevision, found $actualRevision."
    }
}

function Prepare-ZoomItDriver {
    & $driverScript
    if ($LASTEXITCODE -ne 0) {
        throw 'ZoomIt virtual-display driver preparation failed.'
    }

    if (-not $StageZoomItDriver) {
        return
    }

    $argumentList = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        "`"$driverScript`"",
        '-Stage'
    )
    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argumentList -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Elevated driver staging failed with exit code $($process.ExitCode)."
    }
}

function Build-Directory {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [string[]] $ExtraArguments = @()
    )

    Write-Host "`n=== Building $Path ===" -ForegroundColor Cyan
    & $buildScript -Platform $Platform -Configuration Debug -Path $Path @ExtraArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $Path."
    }
}

function Build-PowerScripts {
    $nugetConfig = Join-Path $powerScriptsRoot 'nuget.config'
    $projectDirectories = @(
        'PowerScripts.Core',
        'PowerScripts.Host',
        'PowerScripts.PromptUI',
        'PowerScripts.Core.Tests'
    )

    foreach ($directory in $projectDirectories) {
        $project = Get-ChildItem -Path (Join-Path $powerScriptsRoot $directory) -Filter '*.csproj' -File | Select-Object -First 1
        if (-not $project) {
            throw "No project found in PowerScripts\$directory."
        }
        Invoke-NativeCommand dotnet 'restore' $project.FullName '--configfile' $nugetConfig '--nologo'
        Build-Directory -Path $project.DirectoryName
    }
}

function Build-HackathonProjects {
    Build-PowerScripts

    $projectDirectories = @(
        'src\modules\AdvancedPaste\AdvancedPaste',
        'src\modules\keyboardmanager\KeyboardManagerEditorUI',
        'src\modules\ScreenTranslator\ScreenTranslator',
        'src\modules\ScreenTranslator\ScreenTranslatorModuleInterface',
        'src\modules\ScreenTranslator\ScreenTranslator.UnitTests',
        'src\modules\ZoomIt\ZoomItBreak',
        'src\modules\ZoomIt\ZoomIt',
        'src\modules\ZoomIt\unittests',
        'src\modules\ZoomIt\ZoomItModuleInterface'
    )

    foreach ($relativePath in $projectDirectories) {
        Build-Directory -Path (Join-Path $repoRoot $relativePath)
    }

    $mxcArgument = "/p:MxcRoot=$MxcRoot"
    $tryRunArguments = @($mxcArgument)
    if ($IncludeWslc) {
        $tryRunArguments += '/p:MxcWithWslc=true'
    }
    Build-Directory -Path (Join-Path $repoRoot 'src\modules\TryRun') -ExtraArguments $tryRunArguments

    Build-Directory -Path (Join-Path $repoRoot 'src\runner')
    Build-Directory -Path (Join-Path $repoRoot 'src\settings-ui\Settings.UI')
}

function Start-HackathonPowerToys {
    $existing = Get-Process -Name PowerToys -ErrorAction SilentlyContinue
    if ($existing) {
        $paths = ($existing | ForEach-Object Path | Where-Object { $_ } | Sort-Object -Unique) -join ', '
        throw "PowerToys is already running. Exit it before using -LaunchPowerToys. Running paths: $paths"
    }

    $powerToysPath = Join-Path $repoRoot "$Platform\Debug\PowerToys.exe"
    if (-not (Test-Path $powerToysPath -PathType Leaf)) {
        throw "The Debug runner was not found at $powerToysPath."
    }

    $process = Start-Process -FilePath $powerToysPath -WorkingDirectory (Split-Path $powerToysPath) -PassThru
    Start-Sleep -Seconds 15
    if (-not (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) {
        throw "PowerToys exited during startup. Check the Runner log under LocalAppData\Microsoft\PowerToys\RunnerLogs."
    }

    Write-Host "PowerToys is running from $powerToysPath (PID $($process.Id))." -ForegroundColor Green
}

Assert-Command git
Initialize-Rust
Initialize-Mxc

if ($PrepareZoomItDriver -or $StageZoomItDriver) {
    Prepare-ZoomItDriver
}

if ($Build) {
    Assert-Command dotnet
    Build-HackathonProjects
}

if ($LaunchPowerToys) {
    Start-HackathonPowerToys
}

Write-Host "`nHackathon SALT 2026 setup completed." -ForegroundColor Green
if ($Build -or $LaunchPowerToys) {
    Write-Host "PowerToys Debug output: $(Join-Path $repoRoot "$Platform\Debug")"
}
Write-Host "Pinned MXC checkout: $MxcRoot"
