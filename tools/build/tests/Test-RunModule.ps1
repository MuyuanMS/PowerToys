param(
    [string]$SourceRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ReceiptPath
)
$ErrorActionPreference = 'Stop'
$results = [System.Collections.Generic.List[object]]::new()
function Assert-Check([string]$Name, [bool]$Passed) {
    $results.Add([pscustomobject]@{ name = $Name; passed = $Passed })
}
function Read-Ast([string]$Path) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if (@($errors).Count) { throw "Parse errors in $Path" }
    return $ast
}
function Get-Assignment($Ast, [string]$Variable) {
    $node = $Ast.Find({
        param($item)
        $item -is [System.Management.Automation.Language.AssignmentStatementAst] -and $item.Left.Extent.Text -eq $Variable
    }.GetNewClosure(), $true)
    if (-not $node) { throw "Assignment missing: $Variable" }
    return [scriptblock]::Create($node.Extent.Text)
}
$moduleAst = Read-Ast (Join-Path $SourceRoot 'run-module.ps1')
$helperAst = Read-Ast (Join-Path $SourceRoot 'build-common.ps1')
# Load only the two build functions; the real executable is shadowed below.
foreach ($name in @('RunMSBuild', 'RestoreThenBuild')) {
    $functionAst = $helperAst.Find({
        param($item)
        $item -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $item.Name -eq $name
    }.GetNewClosure(), $true)
    . ([scriptblock]::Create($functionAst.Extent.Text))
}
$calls = [System.Collections.Generic.List[object]]::new()
function msbuild.exe {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $calls.Add([pscustomobject]@{ arguments = @($Arguments) })
    $global:LASTEXITCODE = 0
}
function Get-ProjectRepoRoot { return (Resolve-Path $SourceRoot).Path }
function Get-ProjectSolutionDir { return 'C:\PowerToys With Spaces\' }
function New-Item { param($ItemType, $Path, [switch]$Force) }
function Test-Path { param($Path) return $existingPaths -contains $Path }
$script:RepoRoot = (Resolve-Path $SourceRoot).Path
$repoRoot = 'C:\PowerToys'
$Configuration = 'Debug'
$essentialLoop = $moduleAst.Find({
    param($item)
    $item -is [System.Management.Automation.Language.ForEachStatementAst] -and
    $item.Variable.Extent.Text -eq '$e' -and $item.Condition.Extent.Text -eq '$essentials'
}, $true)
if (-not $essentialLoop) { throw 'Essentials loop missing.' }
foreach ($Platform in @('x64', 'ARM64')) {
    . (Get-Assignment $moduleAst '$essentials')
    . (Get-Assignment $moduleAst '$runnerDir')
    . (Get-Assignment $moduleAst '$outDir')
    Assert-Check "$Platform runner cache points at built runner" ($essentials[0].Exe -eq "src\runner\$Platform\Debug\PowerToys.exe")
    Assert-Check "$Platform settings and quick access follow platform" (
        $essentials[1].Exe -eq "$Platform\Debug\WinUI3Apps\PowerToys.Settings.exe" -and
        $essentials[2].Exe -eq "$Platform\Debug\WinUI3Apps\PowerToys.QuickAccess.exe"
    )
    Assert-Check "$Platform runner source and assembled layout follow platform" (
        $runnerDir -eq "C:\PowerToys\src\runner\$Platform\Debug" -and $outDir -eq "C:\PowerToys\$Platform\Debug"
    )
    $calls.Clear()
    $existingPaths = @(
        "C:\PowerToys\src\runner\$Platform\Debug\PowerToys.exe",
        "C:\PowerToys\$Platform\Debug\WinUI3Apps\PowerToys.Settings.exe",
        "C:\PowerToys\$Platform\Debug\WinUI3Apps\PowerToys.QuickAccess.exe"
    )
    . ([scriptblock]::Create($essentialLoop.Extent.Text))
    Assert-Check "$Platform cached essentials are not rebuilt" ($calls.Count -eq 0)
}
$calls.Clear()
$Platform = 'x64'
$projects = @([pscustomobject]@{ FullName = 'C:\PowerToys With Spaces\src\modules\peek\Peek.Common.UnitTests\Peek.Common.UnitTests.csproj' })
$extraArgsString = '/p:BuildTests=true'
$projectLoop = $moduleAst.Find({
    param($item)
    $item -is [System.Management.Automation.Language.ForEachStatementAst] -and
    $item.Variable.Extent.Text -eq '$project' -and $item.Condition.Extent.Text -eq '$projects'
}, $true)
if (-not $projectLoop) { throw 'Module project loop missing.' }
. ([scriptblock]::Create($projectLoop.Extent.Text))
Assert-Check 'Module invokes restore and build' ($calls.Count -eq 2)
foreach ($call in $calls) {
    $properties = @($call.arguments | Where-Object { $_ -like '/p:SolutionDir=*' })
    Assert-Check 'Spaced SolutionDir arrives as one complete argument' (
        $properties.Count -eq 1 -and $properties[0] -eq '/p:SolutionDir=C:\PowerToys With Spaces\'
    )
    Assert-Check 'Module ExtraArgs remain forwarded' ($call.arguments -contains '/p:BuildTests=true')
}
$calls.Clear()
RestoreThenBuild 'C:\PowerToys\module.csproj' '/p:BuildTests=false' 'x64' 'Release'
Assert-Check 'Legacy helper call still restores then builds' ($calls.Count -eq 2)
Assert-Check 'Legacy arguments and configuration retained' (
    $calls[0].arguments -contains '/t:restore' -and
    $calls[1].arguments -contains '/p:Configuration=Release' -and
    $calls[1].arguments -contains '/p:BuildTests=false'
)
$calls.Clear()
RestoreThenBuild 'C:\PowerToys\module.csproj' $null 'x64' 'Debug' $true
Assert-Check 'Legacy RestoreOnly remains restore-only' ($calls.Count -eq 1 -and $calls[0].arguments -contains '/t:restore')
$supportsSolutionDir = @($helperAst.Find({
    param($item)
    $item -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $item.Name -eq 'RestoreThenBuild'
}, $true).Body.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'SolutionDir' }).Count -eq 1
if ($supportsSolutionDir) {
    $calls.Clear()
    RestoreThenBuild 'C:\PowerToys\module.csproj' '/p:SolutionDir=C:\Override\' 'x64' 'Debug' $true -SolutionDir 'C:\PowerToys With Spaces\'
    $solutionArgs = @($calls[0].arguments | Where-Object { $_ -like '/p:SolutionDir=*' })
    Assert-Check 'Explicit caller SolutionDir override remains last' (
        $solutionArgs.Count -eq 2 -and $solutionArgs[0] -eq '/p:SolutionDir=C:\PowerToys With Spaces\' -and
        $solutionArgs[1] -eq '/p:SolutionDir=C:\Override\'
    )
}
if ($ReceiptPath) {
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
}
$failed = @($results | Where-Object { -not $_.passed })
Write-Output "$($results.Count - $failed.Count)/$($results.Count) checks passed."
if ($failed.Count) {
    $failed | ForEach-Object { Write-Output "FAIL: $($_.name)" }
    exit 1
}
