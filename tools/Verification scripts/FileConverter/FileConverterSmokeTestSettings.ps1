function Enable-FileConverterForSmokeTest {
    $settingsPath = Join-Path $env:LOCALAPPDATA "Microsoft\PowerToys\settings.json"
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        throw "PowerToys settings file not found at: $settingsPath"
    }

    $snapshot = [pscustomobject]@{
        Path = $settingsPath
        Bytes = [System.IO.File]::ReadAllBytes($settingsPath)
    }

    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if ($null -eq $settings.enabled) {
        $settings | Add-Member -NotePropertyName enabled -NotePropertyValue ([pscustomobject]@{})
    }

    $settings.enabled | Add-Member -NotePropertyName FileConverter -NotePropertyValue $true -Force
    [System.IO.File]::WriteAllText(
        $settingsPath,
        ($settings | ConvertTo-Json -Depth 100),
        [System.Text.UTF8Encoding]::new($false))

    return $snapshot
}

function Restore-FileConverterSmokeTestSettings {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Snapshot
    )

    [System.IO.File]::WriteAllBytes($Snapshot.Path, $Snapshot.Bytes)
}
