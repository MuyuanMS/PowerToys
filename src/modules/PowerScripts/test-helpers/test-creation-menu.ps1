# Start with the Editor library open. Requires winapp on PATH.
# Exercises creation forms without creating, approving, or running scripts.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int]$AppPid
)

$ErrorActionPreference = 'Stop'
$winapp = (Get-Command winapp -ErrorAction Stop).Source
$app = Get-Process -Id $AppPid
$root = Split-Path (Split-Path $app.Path -Parent) -Parent
$hostExe = Join-Path $root 'PowerScripts\PowerScripts.Host.exe'
$json = & $hostExe template cli-list
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to read the matching Host CLI recipe catalogue.'
}
$recipes = @(($json -join "`n") | ConvertFrom-Json)
if ($recipes.Count -eq 0) {
    throw 'The CLI recipe catalogue is empty.'
}

function Invoke-UI {
    param([string[]]$Arguments)
    $output = & $winapp ui @Arguments -a $AppPid 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join "`n")
    }
}

function Open-CreationMenu {
    Invoke-UI @('invoke', 'PowerScriptsCreateScript')
    Invoke-UI @('wait-for', 'PowerScriptsNewScriptChoice', '-t', '3000')
    Invoke-UI @('wait-for', 'PowerScriptsNewOperationChoice', '-t', '3000')
}

function Open-GeneralScript {
    Open-CreationMenu
    Invoke-UI @('click', 'PowerScriptsNewScriptChoice')
    Invoke-UI @('wait-for', 'PowerScriptsTemplateRuntime', '-p', 'IsEnabled', '--value', 'True', '-t', '3000')
    Invoke-UI @('wait-for', 'PowerScriptsTemplateContent', '--gone', '-t', '3000')
    Invoke-UI @('wait-for', 'PowerScriptsTemplateCliModule', '--gone', '-t', '3000')
}

function Open-PowerToysAction {
    Open-CreationMenu
    Invoke-UI @('click', 'PowerScriptsNewOperationChoice')
    Invoke-UI @('wait-for', 'PowerScriptsTemplateCliModule', '-p', 'IsEnabled', '--value', 'True', '-t', '10000')
    Invoke-UI @('wait-for', 'PowerScriptsTemplateRuntime', '-p', 'IsEnabled', '--value', 'False', '-t', '3000')
    Invoke-UI @('wait-for', 'PowerScriptsTemplateContent', '--gone', '-t', '3000')
}

function Back-ToLibrary {
    Invoke-UI @('invoke', 'PowerScriptsTemplatesBack')
    Invoke-UI @('wait-for', 'PowerScriptsCreateScript', '-t', '3000')
}

function Select-ArgumentSource {
    param([string]$Control, [string]$Source)
    Invoke-UI @('scroll-into-view', $Control)
    Invoke-UI @('invoke', $Control)
    $json = & $winapp ui list-windows -a $AppPid --json
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to find the argument-source popup.'
    }
    $popup = @(($json | ConvertFrom-Json) | Where-Object label -eq 'popup')
    if ($popup.Count -ne 1) {
        throw "Expected one argument-source popup; found $($popup.Count)."
    }
    $output = & $winapp ui click $Source -w $popup[0].hwnd 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join "`n")
    }
    Invoke-UI @('wait-for', $Control, '--value', $Source, '-t', '3000')
}

Open-GeneralScript
Write-Output 'PASS: Script opens the general form without a second script-type selector.'
Back-ToLibrary
Open-PowerToysAction
Write-Output 'PASS: PowerToys action opens the full CLI recipe form directly.'

$source = 'PowerScriptsCliArgument_ModuleName_Source'
$fixedValue = 'PowerScriptsCliArgument_ModuleName_Text'
Invoke-UI @('set-value', $fixedValue, 'FancyZones')
Select-ArgumentSource $source 'Script input'
Invoke-UI @('wait-for', $fixedValue, '--gone', '-t', '3000')
Select-ArgumentSource $source 'Ask when the script runs'
Invoke-UI @('wait-for', $fixedValue, '--gone', '-t', '3000')
Select-ArgumentSource $source 'Fixed value'
Invoke-UI @('wait-for', $fixedValue, '--value', 'FancyZones', '-t', '3000')
Write-Output 'PASS: Fixed/input/ask sources work and preserve the configured fixed value.'

$tested = 0
$modules = @($recipes | Group-Object moduleId)
foreach ($module in $modules) {
    Invoke-UI @('scroll-into-view', 'PowerScriptsTemplateCliModule')
    Invoke-UI @('invoke', 'PowerScriptsTemplateCliModule')
    Invoke-UI @('wait-for', "PowerScriptsTemplateCliModule_$($module.Name)", '-t', '3000')
    Invoke-UI @('click', "PowerScriptsTemplateCliModule_$($module.Name)")
    foreach ($recipe in $module.Group) {
        Invoke-UI @('scroll-into-view', 'PowerScriptsTemplateCliAction')
        Invoke-UI @('invoke', 'PowerScriptsTemplateCliAction')
        Invoke-UI @('wait-for', "PowerScriptsTemplateCliContent_$($recipe.id)", '-t', '3000')
        Invoke-UI @('click', "PowerScriptsTemplateCliContent_$($recipe.id)")
        if ($recipe.parameters.Count -gt 0) {
            $argument = "PowerScriptsCliArgument_$($recipe.parameters[0].name)_Source"
            Invoke-UI @('wait-for', $argument, '-t', '3000')
        }
        ++$tested
    }
}
Write-Output "PASS: All $tested recipes across $($modules.Count) modules expose their argument form."

Back-ToLibrary
Open-GeneralScript
Write-Output 'PASS: Returning from an action does not change general script creation.'
Back-ToLibrary

$app.Refresh()
if ($app.HasExited -or -not $app.Responding) {
    throw 'The Editor did not remain responsive.'
}
Write-Output 'PASS: Editor remains responsive; no script was created or executed.'
