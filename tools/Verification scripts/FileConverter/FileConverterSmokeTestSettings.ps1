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

function Ensure-FileConverterSmokeTestFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        return
    }

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [byte[]]$bmp = @(
        0x42, 0x4D, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
        0x28, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
        0x13, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00
    )
    [System.IO.File]::WriteAllBytes($Path, $bmp)
}
