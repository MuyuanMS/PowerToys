# Copyright (c) Microsoft Corporation
# The Microsoft Corporation licenses this file to you under the MIT license.
# See the LICENSE file in the project root for more information.

#Requires -Version 7.0
$scriptPath = Join-Path $PSScriptRoot '..\runUiTestAsUser.ps1'
$powerShell = (Get-Process -Id $PID).Path

Describe 'runUiTestAsUser' {
    It 'reports a mismatched interactive identity without starting a test executable' {
        $runDirectory = Join-Path $TestDrive 'identity-failure'
        New-Item -ItemType Directory -Path $runDirectory | Out-Null
        $requestPath = Join-Path $runDirectory 'request.json'
        @{
            RunId = 'identity-failure'
            RunDirectory = $runDirectory
            InteractiveUser = 'InvalidDomain\InvalidUiTestUser'
            TestExecutable = (Join-Path $TestDrive 'must-not-run.exe')
            Environment = @{}
        } | ConvertTo-Json | Set-Content -LiteralPath $requestPath

        & $powerShell -NoProfile -NonInteractive -File $scriptPath -RequestPath $requestPath

        $LASTEXITCODE | Should Be 1
        $status = Get-Content (Join-Path $runDirectory 'status.json') -Raw | ConvertFrom-Json
        $status.RunId | Should Be 'identity-failure'
        $status.ExitCode | Should Be 1
        $status.Error | Should Match 'requested non-elevated interactive user'
        Test-Path (Join-Path $runDirectory 'status.tmp') | Should Be $false
        Test-Path (Join-Path $runDirectory 'stdout.log') | Should Be $false
    }

    It 'rejects a missing executable before scheduling any desktop work' {
        $resultsDirectory = Join-Path $TestDrive 'must-not-create'
        & $powerShell -NoProfile -NonInteractive -File $scriptPath `
            -TestExecutable (Join-Path $TestDrive 'missing.exe') `
            -ResultsDirectory $resultsDirectory 2>&1 | Out-Null

        $LASTEXITCODE | Should Be 1
        Test-Path $resultsDirectory | Should Be $false
    }
}
