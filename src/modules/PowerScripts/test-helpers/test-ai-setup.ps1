# Open Settings > PowerScripts and an existing script's details in the matching Editor.
# Run with powershell.exe -STA. Does not install skills, approve scripts, or run scripts.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int]$SettingsAppPid,
    [Parameter(Mandatory)]
    [int]$EditorAppPid,
    [Parameter(Mandatory)]
    [string]$ScriptEntryPath,
    [switch]$SkipLauncherActivation
)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this test with powershell.exe -STA.'
}
Add-Type -AssemblyName System.Windows.Forms
$winapp = (Get-Command winapp -ErrorAction Stop).Source
$ScriptEntryPath = [IO.Path]::GetFullPath($ScriptEntryPath)
$appDirectory = Split-Path (Get-Process -Id $EditorAppPid).Path -Parent
$bundle = Join-Path $appDirectory 'PowerScriptsAgentSkill'
if (-not (Test-Path -LiteralPath (Join-Path $bundle 'SKILL.md'))) {
    $bundle = [IO.Path]::GetFullPath((Join-Path $appDirectory '..\PowerScripts\AgentSkill'))
}
if (-not (Test-Path -LiteralPath (Join-Path $bundle 'SKILL.md'))) {
    throw 'The matching Editor has no bundled skill.'
}

function Invoke-UI {
    param([int]$AppId, [string[]]$Arguments)
    $output = & $winapp ui @Arguments -a $AppId 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join "`n")
    }
}

function Assert-CopiedInstructions {
    $text = [Windows.Forms.Clipboard]::GetText()
    foreach ($expected in @($bundle, $ScriptEntryPath, "$ScriptEntryPath.tool.json")) {
        if (-not $text.Contains($expected)) {
            throw 'The copied instructions do not identify the bundle and selected script source/metadata.'
        }
    }
    return $text
}

Invoke-UI $SettingsAppPid @('wait-for', 'PowerScriptsOpenEditor', '-t', '3000')
Invoke-UI $SettingsAppPid @('wait-for', 'PowerScriptsCopySkillInstructionsButton', '--gone', '-t', '3000')
Invoke-UI $SettingsAppPid @('wait-for', 'PowerScriptsAgentSkillFeedback', '--gone', '-t', '3000')
if ($SkipLauncherActivation) {
    Write-Output 'SKIP: Launcher placement verified; physical activation requires an interactive desktop.'
}
else {
    Invoke-UI $SettingsAppPid @('click', 'PowerScriptsOpenEditor')
    Write-Output 'PASS: Settings offers only the editor launcher; it opens the existing editor.'
}
Invoke-UI $EditorAppPid @('wait-for', 'PowerScriptsHeroSetUpAI', '-p', 'IsEnabled', '--value', 'True', '-t', '5000')

$original = [Windows.Forms.Clipboard]::GetDataObject()
$backup = New-Object Windows.Forms.DataObject
if ($null -ne $original) {
    foreach ($format in $original.GetFormats($false)) {
        $backup.SetData($format, $false, $original.GetData($format, $false))
    }
}
$lastCopied = $null
try {
    Invoke-UI $EditorAppPid @('invoke', 'PowerScriptsHeroSetUpAI')
    $lastCopied = [Windows.Forms.Clipboard]::GetText()
    $heroInstructions = Assert-CopiedInstructions
    Invoke-UI $EditorAppPid @('wait-for', 'PowerScriptsAgentSkillFeedback', '-t', '3000')
    Write-Output 'PASS: The script header copies AI setup instructions with the selected script context.'

    Invoke-UI $EditorAppPid @('invoke', 'PowerScriptsDetailsCode', '--action', 'select')
    Invoke-UI $EditorAppPid @('wait-for', 'PowerScriptsEditScript', '-t', '3000')
    Invoke-UI $EditorAppPid @('wait-for', 'PowerScriptsEditMetadata', '-t', '3000')
    Invoke-UI $EditorAppPid @('invoke', 'PowerScriptsSetUpAI')
    $lastCopied = [Windows.Forms.Clipboard]::GetText()
    if ((Assert-CopiedInstructions) -ne $heroInstructions) {
        throw 'Header and code-toolbar AI setup actions copied different instructions.'
    }
    Write-Output 'PASS: The AI setup action beside Edit script/Edit metadata copies the same instructions.'
}
finally {
    if ($null -ne $lastCopied -and [Windows.Forms.Clipboard]::GetText() -eq $lastCopied) {
        if ($null -eq $original) {
            [Windows.Forms.Clipboard]::Clear()
        }
        else {
            [Windows.Forms.Clipboard]::SetDataObject($backup, $true)
        }
    }
}
