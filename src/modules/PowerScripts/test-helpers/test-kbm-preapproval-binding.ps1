param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$HostPath,
    [Parameter(Mandatory)][string]$ProbeId,
    [Parameter(Mandatory)][string]$ResultsPath,
    [switch]$AllowTemporaryBinding,
    [scriptblock]$VerifySavedBinding
)

$ErrorActionPreference = 'Stop'
if (-not $AllowTemporaryBinding) {
    throw 'This test saves, toggles and deletes a temporary Ctrl(Left)+F24 binding. Pass -AllowTemporaryBinding to consent.'
}

$winapp = (Get-Command winapp.exe -ErrorAction Stop).Source
$window = [Diagnostics.Process]::GetProcessById($AppPid).MainWindowHandle.ToInt64()
$settingsPath = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\Keyboard Manager\editorSettings.json'
$before = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
if (@($before.ShortcutSettingsDictionary.Values | Where-Object { $_.Shortcut.OriginalKeys -match '(^|;)135(;|$)' }).Count) {
    throw 'F24 is already used; this test will not change an existing shortcut.'
}

function Get-Probe {
    $scripts = @(& $HostPath list --json --include-disabled --include-unconfigured | ConvertFrom-Json)
    if ($LASTEXITCODE) { throw 'Host discovery failed.' }
    $scripts | Where-Object id -EQ $ProbeId
}

function Invoke-UI([string[]]$Arguments) {
    $output = & $winapp ui @Arguments -w $window
    if ($LASTEXITCODE) { throw "UI command failed: $($Arguments -join ' ')" }
    $output
}

function Get-Nodes($nodes) {
    foreach ($node in $nodes) {
        $node
        if ($node.children) { Get-Nodes $node.children }
    }
}

function Get-Tree([string]$Selector) {
    $arguments = @('inspect')
    if ($Selector) { $arguments += $Selector }
    $tree = Invoke-UI ($arguments + @('--depth', '20', '--json')) | ConvertFrom-Json
    @($tree.windows | ForEach-Object { Get-Nodes $_.elements })
}

function Select-Key([string]$Label, [string]$Search) {
    $picker = Get-Tree 'TriggerKeyToggleBtn' | Where-Object {
        $_.automationId -eq 'KeyButton' -and
        @(Get-Nodes $_.children | Where-Object automationId -EQ 'SelectKeyPlaceholderText').Count
    } | Select-Object -First 1
    if (-not $picker) { throw 'The empty trigger-key picker was not found.' }
    [void](Invoke-UI @('invoke', $picker.selector))
    $searchBox = Get-Tree 'KeySearchBox' | Where-Object type -EQ 'Edit' | Select-Object -First 1
    [void](Invoke-UI @('set-value', $searchBox.selector, $Search))
    [void](Invoke-UI @('invoke', $Label, '--type', 'ListItem'))
}

function Get-Binding {
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
    @($settings.ShortcutSettingsDictionary.Values | Where-Object {
        $_.Shortcut.ProgramArgs -match ('^run ' + [regex]::Escape($ProbeId) + ' ')
    })
}

function Get-ProbeRow {
    Get-Tree 'PowerScriptShortcutsList' | Where-Object {
        $_.type -eq 'ListItem' -and
        @(Get-Nodes $_.children | Where-Object name -EQ $probe.name).Count
    } | Select-Object -First 1
}

function Wait-BindingState([bool]$Active) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $binding = @(Get-Binding)
        if ($binding.Count -eq 1 -and $binding[0].IsActive -eq $Active) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "The saved probe binding did not become active=$Active."
}

$probe = Get-Probe
if (-not $probe -or $probe.trusted -or $probe.io.input -ne 'none' -or $probe.configurationErrors.Count) {
    throw 'Provide a valid, deliberately unapproved no-input probe action.'
}
if (@(Get-Binding).Count) { throw 'The probe already has a binding; refusing to modify it.' }
$results = @()
try {
    [void](Invoke-UI @('invoke', 'NewRemappingBtn'))
    Select-Key 'Ctrl (Left)' 'Ctrl'
    Select-Key 'F24' 'F24'
    [void](Invoke-UI @('invoke', 'ActionTypeComboBox'))
    $action = Get-Tree 'ActionTypeComboBox' | Where-Object {
        $_.type -eq 'ListItem' -and @(Get-Nodes $_.children | Where-Object name -EQ 'PowerScript').Count
    } | Select-Object -First 1
    if (-not $action) { throw 'The PowerScript action was not found.' }
    [void](Invoke-UI @('invoke', $action.selector))
    [void](Invoke-UI @('invoke', 'PowerScriptComboBox'))
    [void](Invoke-UI @('invoke', $probe.name, '--type', 'ListItem'))
    [void](Invoke-UI @('wait-for', 'PrimaryButton', '-p', 'IsEnabled', '--value', 'True', '-t', '3000'))
    [void](Invoke-UI @('invoke', 'PrimaryButton'))
    Wait-BindingState $true
    $binding = @(Get-Binding)[0]
    if ($binding.Shortcut.ProgramArgs -notmatch ' --consumer keyboard-manager' -or
        $binding.Shortcut.ProgramArgs -match ' --no-consent') {
        throw 'The saved binding must allow ordinary first-use Host approval.'
    }
    $results += @{ name = 'Save an active binding before approval'; status = 'PASS' }
    if ($VerifySavedBinding) {
        & $VerifySavedBinding $binding
        $results += @{ name = 'Saved binding first-use verification'; status = 'PASS' }
    }

    foreach ($active in @($false, $true)) {
        $row = Get-ProbeRow
        if (-not $row) { throw 'The probe binding row was not found.' }
        $toggle = Get-Nodes $row.children | Where-Object className -EQ 'ToggleSwitch' | Select-Object -First 1
        [void](Invoke-UI @('invoke', $toggle.selector))
        Wait-BindingState $active
    }
    $results += @{ name = 'Disable and reactivate before approval'; status = 'PASS' }

    $stderrPath = $ResultsPath + '.stderr'
    try {
        $stdout = & $HostPath run $ProbeId --consumer command-line --no-consent 2> $stderrPath
        $exitCode = $LASTEXITCODE
        $stderr = Get-Content -LiteralPath $stderrPath -Raw
        if ($exitCode -ne 3 -or $stdout -or $stderr -notmatch 'not trusted' -or (Get-Probe).trusted) {
            throw 'Unapproved execution was not refused without changing trust.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $stderrPath) { Remove-Item -LiteralPath $stderrPath }
    }
    $results += @{ name = 'Explicit noninteractive execution remains refused'; status = 'PASS' }
}
finally {
    if (@(Get-Binding).Count) {
        $row = Get-ProbeRow
        if (-not $row) { throw 'The temporary row could not be found for normal-UI cleanup.' }
        $menu = Get-Nodes $row.children | Where-Object className -EQ 'Button' | Select-Object -First 1
        [void](Invoke-UI @('invoke', $menu.selector))
        [void](Invoke-UI @('invoke', 'Delete', '--type', 'MenuItem'))
        [void](Invoke-UI @('wait-for', 'PrimaryButton', '-t', '3000'))
        [void](Invoke-UI @('invoke', 'PrimaryButton'))
        for ($attempt = 0; $attempt -lt 30 -and @(Get-Binding).Count; $attempt++) {
            Start-Sleep -Milliseconds 100
        }
        if (@(Get-Binding).Count) { throw 'The temporary binding was not removed.' }
    }
    $after = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($id in $before.ShortcutSettingsDictionary.Keys) {
        if (($before.ShortcutSettingsDictionary[$id] | ConvertTo-Json -Depth 30 -Compress) -ne
            ($after.ShortcutSettingsDictionary[$id] | ConvertTo-Json -Depth 30 -Compress)) {
            throw "A preexisting binding changed: $id"
        }
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ResultsPath -Encoding utf8
}

Write-Output 'PASS: preapproval save/activation, interactive binding arguments, explicit noninteractive refusal, and existing-binding preservation.'
