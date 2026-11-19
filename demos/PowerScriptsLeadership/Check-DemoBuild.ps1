#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BuildRoot,
    [ValidateSet('ARM64', 'x64')]
    [string]$Architecture = 'ARM64'
)

$ErrorActionPreference = 'Stop'
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$expected = if ($Architecture -eq 'ARM64') { 0xAA64 } else { 0x8664 }
$required = @(
    'PowerToys.exe',
    'PowerScripts\PowerScripts.Host.exe',
    'PowerScripts\wxc-exec.exe',
    'WinUI3Apps\PowerToys.PowerScriptsEditor.exe',
    'WinUI3Apps\PowerToys.AdvancedPaste.exe',
    'WinUI3Apps\PowerToys.AdvancedPaste.Cli.exe',
    'PowerToys.LightSwitch.Cli.exe'
)
$results = foreach ($relative in $required) {
    $path = Join-Path $BuildRoot $relative
    if (-not [IO.File]::Exists($path)) {
        throw "Missing required demo binary: $path"
    }
    $stream = [IO.File]::OpenRead($path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid PE file: $path" }
        $stream.Position = 0x3C
        $stream.Position = $reader.ReadInt32()
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $path" }
        $machine = $reader.ReadUInt16()
        if ($machine -ne $expected) {
            throw ('Wrong architecture for {0}: PE machine 0x{1:X4}; expected {2}.' -f $relative, $machine, $Architecture)
        }
    }
    finally {
        $reader.Dispose()
    }
    [pscustomobject]@{ Binary = $relative; Architecture = $Architecture; Present = $true }
}
$results | Format-Table -AutoSize
$hostPath = Join-Path $BuildRoot 'PowerScripts\PowerScripts.Host.exe'
$json = & $hostPath mxc-support --json
if ($LASTEXITCODE -ne 0) { throw 'MXC support probe failed.' }
$support = $json | ConvertFrom-Json
$support | Format-List
if (-not $support.isSupported) {
    throw 'MXC is unavailable. Diagnose the prerequisite; do not disable containment or restrictions as a workaround.'
}
Write-Host 'Core/editor/CLI architecture and MXC support checks passed.'
Write-Host 'This does NOT prove shell registration, consumer integration, script trust, or successful script execution.'
