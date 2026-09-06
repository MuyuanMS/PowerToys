# Copyright (c) Microsoft Corporation
# The Microsoft Corporation licenses this file to you under the MIT license.
# See the LICENSE file in the project root for more information.

#Requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'Controller')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Controller')]
    [string] $TestExecutable,
    [Parameter(Mandatory, ParameterSetName = 'Controller')]
    [string] $ResultsDirectory,
    [Parameter(ParameterSetName = 'Controller')]
    [string] $InteractiveUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name,
    [Parameter(ParameterSetName = 'Controller')]
    [string] $Filter,
    [Parameter(ParameterSetName = 'Controller')]
    [ValidateRange(1, 120)]
    [int] $TimeoutMinutes = 45,
    [Parameter(Mandatory, ParameterSetName = 'Worker')]
    [string] $RequestPath
)

$ErrorActionPreference = 'Stop'

if ($PSCmdlet.ParameterSetName -eq 'Worker') {
    $request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
    $exitCode = 1
    $failure = $null
    $process = $null
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $sessionId = (Get-Process -Id $PID).SessionId
        $elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        "User=$($identity.Name); Session=$sessionId; Elevated=$elevated" |
            Set-Content -LiteralPath (Join-Path $request.RunDirectory 'desktop.txt')
        if ($elevated -or $sessionId -eq 0 -or $identity.Name -ine $request.InteractiveUser) {
            throw 'UI tests require the requested non-elevated interactive user.'
        }
        if (-not @(Get-Process explorer -ErrorAction SilentlyContinue | Where-Object SessionId -eq $sessionId)) {
            throw 'The interactive test session has no Explorer desktop.'
        }

        foreach ($property in $request.Environment.PSObject.Properties) {
            [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value, 'Process')
        }
        $start = [Diagnostics.ProcessStartInfo]::new([string]$request.TestExecutable)
        $start.WorkingDirectory = Split-Path $request.TestExecutable -Parent
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @('--report-trx', '--results-directory', [string]$request.ResultsDirectory)) {
            $start.ArgumentList.Add($argument)
        }
        if ($request.Filter) {
            $start.ArgumentList.Add('--filter')
            $start.ArgumentList.Add([string]$request.Filter)
        }
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit([int]$request.TimeoutMinutes * 60 * 1000)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $request.RunDirectory 'stdout.log')
        $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $request.RunDirectory 'stderr.log')
        if ($timedOut) {
            throw "UI test executable exceeded $($request.TimeoutMinutes) minutes."
        }
        $exitCode = $process.ExitCode
    }
    catch {
        $failure = $_.ToString()
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
        $status = @{
            RunId = $request.RunId
            ExitCode = $exitCode
            Error = $failure
        }
        $temporaryStatus = Join-Path $request.RunDirectory 'status.tmp'
        $status | ConvertTo-Json | Set-Content -LiteralPath $temporaryStatus
        Move-Item -LiteralPath $temporaryStatus -Destination (Join-Path $request.RunDirectory 'status.json')
    }
    exit $exitCode
}

$TestExecutable = (Resolve-Path -LiteralPath $TestExecutable).Path
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$runId = [guid]::NewGuid().ToString('N')
$runDirectory = Join-Path $ResultsDirectory "InteractiveUiTest-$runId"
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$acl = Get-Acl -LiteralPath $ResultsDirectory
$rule = [Security.AccessControl.FileSystemAccessRule]::new(
    $InteractiveUser, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.AddAccessRule($rule)
Set-Acl -LiteralPath $ResultsDirectory -AclObject $acl
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$requestPath = Join-Path $runDirectory 'request.json'
$statusPath = Join-Path $runDirectory 'status.json'

# Scheduled tasks inherit the desktop environment, not the pipeline agent's private runtime/tools.
# Transfer only test configuration; never serialize the agent's credentials or complete environment.
$environment = @{}
foreach ($name in @(
    'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64',
    'WINAPP_CLI_PATH', 'WINAPP_CLI_INVOKE_TIMEOUT_SECONDS',
    'platform', 'TF_BUILD', 'useInstallerForTest', 'POWERTOYS_INSTALL_DIR'
)) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ($null -ne $value) {
        $environment[$name] = $value
    }
}
@{
    RunId = $runId
    RunDirectory = $runDirectory
    TestExecutable = $TestExecutable
    ResultsDirectory = $ResultsDirectory
    InteractiveUser = $InteractiveUser
    Filter = $Filter
    TimeoutMinutes = $TimeoutMinutes
    Environment = $environment
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $requestPath

$taskName = "PowerToys-InteractiveUiTest-$runId"
$powerShell = (Get-Process -Id $PID).Path
$arguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -File "{0}" -RequestPath "{1}"' -f $PSCommandPath, $requestPath
$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$principal = New-ScheduledTaskPrincipal -UserId $InteractiveUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes ($TimeoutMinutes + 1)) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$registered = $false
try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings | Out-Null
    $registered = $true
    Write-Host "Running $TestExecutable non-elevated as $InteractiveUser. Launcher evidence: $runDirectory"
    $started = [DateTime]::Now
    $deadline = $started.AddMinutes($TimeoutMinutes + 1)
    $launchObserved = $false
    Start-ScheduledTask -TaskName $taskName
    while (-not (Test-Path -LiteralPath $statusPath)) {
        Start-Sleep -Seconds 5
        $task = Get-ScheduledTask -TaskName $taskName
        $info = Get-ScheduledTaskInfo -TaskName $taskName
        $launchObserved = $launchObserved -or $task.State -eq 'Running' -or
            (Test-Path -LiteralPath (Join-Path $runDirectory 'desktop.txt')) -or $info.LastRunTime.Year -ge 2000
        if ($task.State -ne 'Running' -and $launchObserved) {
            if (-not (Test-Path -LiteralPath $statusPath)) {
                throw "Interactive task exited without status (result $($info.LastTaskResult)). Evidence: $runDirectory"
            }
        }
        if (-not $launchObserved -and [DateTime]::Now -gt $started.AddSeconds(30)) {
            throw "The test task never entered the interactive desktop. Evidence: $runDirectory"
        }
        if ([DateTime]::Now -gt $deadline) {
            throw "Timed out waiting for the interactive test task. Evidence: $runDirectory"
        }
    }
    $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
    if ($status.RunId -ne $runId) {
        throw 'Interactive test status does not match the requested run.'
    }
    foreach ($name in @('desktop.txt', 'stdout.log', 'stderr.log')) {
        $path = Join-Path $runDirectory $name
        if (Test-Path -LiteralPath $path) {
            Get-Content -LiteralPath $path | Write-Host
        }
    }
    if ($status.Error) {
        throw "Interactive UI-test launch failed: $($status.Error)"
    }
    exit [int]$status.ExitCode
}
finally {
    if ($registered) {
        if ((Get-ScheduledTask -TaskName $taskName).State -eq 'Running') {
            Stop-ScheduledTask -TaskName $taskName
        }
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
}
