#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$HostPath,
    [string]$ScriptsRoot
)

$ErrorActionPreference = 'Stop'
$HostPath = (Resolve-Path -LiteralPath $HostPath).Path
if ([string]::IsNullOrWhiteSpace($ScriptsRoot)) {
    $ScriptsRoot = $env:POWERSCRIPTS_ROOT
    if ([string]::IsNullOrWhiteSpace($ScriptsRoot)) {
        $settingsPath = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\PowerScripts\config.json'
        if (Test-Path -LiteralPath $settingsPath) {
            $ScriptsRoot = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).scriptsRoot
        }
    }
    if ([string]::IsNullOrWhiteSpace($ScriptsRoot)) {
        $ScriptsRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\PowerScripts\scripts'
    }
}
$ScriptsRoot = [IO.Path]::GetFullPath($ScriptsRoot)

function Read-KitFile([string]$RelativePath) {
    $path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $RelativePath))
    $prefix = $PSScriptRoot.TrimEnd('\') + '\'
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.File]::Exists($path)) {
        throw "Missing or out-of-kit asset: $RelativePath"
    }
    return $path
}

function Invoke-HostJson([string[]]$HostArguments, [string]$JsonInput) {
    $errorFile = [IO.Path]::GetTempFileName()
    try {
        if ($JsonInput) {
            $output = $JsonInput | & $HostPath @HostArguments 2> $errorFile
        }
        else {
            $output = & $HostPath @HostArguments 2> $errorFile
        }
        $exitCode = $LASTEXITCODE
        $diagnostics = [IO.File]::ReadAllText($errorFile)
        if ($exitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($diagnostics)) {
            throw "Host command failed (exit $exitCode): $diagnostics"
        }
        return ($output -join "`n")
    }
    finally {
        [IO.File]::Delete($errorFile)
    }
}

$definitions = @(Get-Content -LiteralPath (Read-KitFile 'Scripts.json') -Raw | ConvertFrom-Json -AsHashtable)
foreach ($definition in $definitions) {
    [void](Read-KitFile $definition.icon)
    if ($definition.implementation) {
        [void](Read-KitFile $definition.implementation)
    }
}

$catalogue = @(Invoke-HostJson @('list', '--json', '--include-disabled', '--include-unconfigured', '--root', $ScriptsRoot) |
    ConvertFrom-Json)
$titles = @($definitions | ForEach-Object { $_.options.projectName })
$existing = @($catalogue | Where-Object name -In $titles)
if ($existing.Count -gt 0) {
    throw ('Demo titles already exist; nothing was overwritten: ' + ($existing.name -join ', '))
}

$installed = @()
foreach ($definition in $definitions) {
    $options = $definition.options
    $options.iconSource = Read-KitFile $definition.icon
    $created = Invoke-HostJson @('template', 'create', '--configure', '--root', $ScriptsRoot) `
        ($options | ConvertTo-Json -Depth 15 -Compress) | ConvertFrom-Json
    $entry = [IO.Path]::GetFullPath($created.entryPath)
    if (-not $entry.StartsWith($ScriptsRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Host returned a package outside the requested scripts root.'
    }
    if ($definition.implementation) {
        [IO.File]::Copy((Read-KitFile $definition.implementation), $entry, $true)
    }
    if ($definition.inputMimeTypes -or $definition.outputMimeTypes) {
        $descriptorPath = $entry + '.tool.json'
        $descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json -AsHashtable
        if ($definition.inputMimeTypes) {
            if ($options.input -ne 'image') { throw 'Input MIME types require an image contract.' }
            $descriptor.inputSchema.properties.image.properties.mimeType.enum = @($definition.inputMimeTypes)
        }
        if ($definition.outputMimeTypes) {
            if ($options.output -ne 'image') { throw 'Output MIME types require an image contract.' }
            $descriptor.outputSchema.properties.image.properties.mimeType.enum = @($definition.outputMimeTypes)
        }
        [IO.File]::WriteAllText($descriptorPath, ($descriptor | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    }
    $installed += [pscustomobject]@{ title = $options.projectName; entryPath = $entry }
}

$catalogue = @(Invoke-HostJson @('list', '--json', '--include-disabled', '--include-unconfigured', '--root', $ScriptsRoot) |
    ConvertFrom-Json)
$result = foreach ($package in $installed) {
    $script = $catalogue | Where-Object entryFullPath -EQ $package.entryPath
    if (-not $script -or $script.configurationErrors.Count -or $script.iconWarning -or $script.trusted) {
        throw "New package did not pass discovery/icon/untrusted-state checks: $($package.title)"
    }
    [pscustomobject]@{
        title = $script.name
        id = $script.id
        entryPath = $script.entryFullPath
        input = $script.io.input
        output = $script.io.output
        trusted = $script.trusted
    }
}
$indexPath = Join-Path $PSScriptRoot 'installed-demo.json'
[IO.File]::WriteAllText($indexPath, (ConvertTo-Json -InputObject @($result) -Depth 8), [Text.UTF8Encoding]::new($false))
$result | Format-Table title, input, output, trusted -AutoSize
Write-Host "Created $($installed.Count) NEW, UNTRUSTED packages in $ScriptsRoot"
Write-Host 'No script was executed; no settings, policy, trust approvals or hotkeys were changed.'
Write-Host "Machine-local IDs: $indexPath"
