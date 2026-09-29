# Copyright (c) Microsoft Corporation.
# The Microsoft Corporation licenses this file to you under the MIT license.

$scriptPath = Join-Path $PSScriptRoot 'Uninstall-PowerToys.ps1'
. $scriptPath

$environmentVariableNames = @(
    'LOCALAPPDATA',
    'ProgramData',
    'ProgramFiles',
    'ProgramFiles(x86)',
    'SystemRoot'
)
$originalEnvironmentVariables = @{}
foreach ($name in $environmentVariableNames) {
    $originalEnvironmentVariables[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

Describe 'Uninstall-PowerToys' {
    AfterAll {
        foreach ($name in $environmentVariableNames) {
            [Environment]::SetEnvironmentVariable(
                $name,
                $originalEnvironmentVariables[$name],
                'Process')
        }
    }

    BeforeEach {
        $env:LOCALAPPDATA = Join-Path $TestDrive 'LocalAppData'
        $env:ProgramData = Join-Path $TestDrive 'ProgramData'
        $env:ProgramFiles = Join-Path $TestDrive 'ProgramFiles'
        ${env:ProgramFiles(x86)} = Join-Path $TestDrive 'ProgramFilesX86'
        $env:SystemRoot = Join-Path $TestDrive 'Windows'

        Mock New-ProtectedDirectory {}
        Mock Stop-PowerToysProcesses {}
        Mock Remove-KnownArtifact {}
        Mock Remove-KnownRegistryValue {}
    }

    It 'does not enter the destructive path with WhatIf' {
        Mock Get-PowerToysMsiProducts { @() }
        Mock Get-PowerToysBundles { @() }
        Mock Test-IsAdministrator { throw 'Should not evaluate elevation during WhatIf.' }

        Invoke-PowerToysCleanup -WhatIf

        Assert-MockCalled Test-IsAdministrator -Times 0
        Assert-MockCalled Stop-PowerToysProcesses -Times 0
        Assert-MockCalled New-ProtectedDirectory -Times 0
    }

    It 'filters machine-wide targets from a non-elevated cleanup pass' {
        $script:productCall = 0
        Mock Get-PowerToysMsiProducts {
            $script:productCall++
            if ($script:productCall -eq 1) {
                return @(
                    [pscustomobject]@{
                        Scope = 'PerUser'
                        ProductCode = '{11111111-1111-1111-1111-111111111111}'
                        UpgradeCode = '{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}'
                        State = 5
                        StateName = 'Default'
                    },
                    [pscustomobject]@{
                        Scope = 'PerMachine'
                        ProductCode = '{22222222-2222-2222-2222-222222222222}'
                        UpgradeCode = '{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}'
                        State = 5
                        StateName = 'Default'
                    })
            }

            return @()
        }
        Mock Get-PowerToysBundles { @() }
        Mock Test-IsAdministrator { $false }
        Mock Test-PowerToysMsiProduct { $true }
        Mock Get-MsiProductProperty { Join-Path $TestDrive 'cached.msi' }
        Mock Copy-MsiForExecution { Join-Path $TestDrive 'staged.msi' }
        Mock Invoke-Uninstaller {}

        Invoke-PowerToysCleanup -Confirm:$false

        Assert-MockCalled Test-PowerToysMsiProduct -Times 1 -ParameterFilter {
            $Product.Scope -eq 'PerUser'
        }
        Assert-MockCalled Test-PowerToysMsiProduct -Times 0 -ParameterFilter {
            $Product.Scope -eq 'PerMachine'
        }
        Assert-MockCalled Invoke-Uninstaller -Times 1
    }

    It 'rejects a staged bundle whose PowerToys identity does not survive the copy' {
        Mock Copy-Item {}
        Mock Test-PowerToysBundleExecutable { $false }

        $message = $null
        try {
            Copy-BundleExecutableForExecution `
                -Path (Join-Path $TestDrive 'source.exe') `
                -DestinationDirectory $TestDrive
        } catch {
            $message = $_.Exception.Message
        }

        $message | Should Match 'Microsoft-signed PowerToys bootstrapper'
    }

    It 'tracks reboot-required and failed uninstall exit codes' {
        $script:rebootRequired = $false
        $script:failures = [System.Collections.Generic.List[string]]::new()

        Add-UninstallerExitCode -ExitCode 3010 -Description 'Reboot uninstall'

        $script:rebootRequired | Should Be $true
        $script:failures.Count | Should Be 0

        Add-UninstallerExitCode -ExitCode 42 -Description 'Failed uninstall'

        $script:failures.Count | Should Be 1
        $script:failures[0] | Should Match 'exit code 42'
    }

    It 'aggregates validation and uninstall failures before returning' {
        $script:productCall = 0
        Mock Get-PowerToysMsiProducts {
            $script:productCall++
            if ($script:productCall -eq 1) {
                return @(
                    [pscustomobject]@{
                        Scope = 'PerUser'
                        ProductCode = '{33333333-3333-3333-3333-333333333333}'
                        UpgradeCode = '{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}'
                        State = 5
                        StateName = 'Default'
                    },
                    [pscustomobject]@{
                        Scope = 'PerUser'
                        ProductCode = '{44444444-4444-4444-4444-444444444444}'
                        UpgradeCode = '{DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD}'
                        State = 5
                        StateName = 'Default'
                    })
            }

            return @()
        }
        Mock Get-PowerToysBundles { @() }
        Mock Test-IsAdministrator { $false }
        Mock Test-PowerToysMsiProduct {
            if ($Product.ProductCode -eq '{33333333-3333-3333-3333-333333333333}') {
                throw 'MSI query failed.'
            }

            return $true
        }
        Mock Get-MsiProductProperty { Join-Path $TestDrive 'cached.msi' }
        Mock Copy-MsiForExecution { Join-Path $TestDrive 'staged.msi' }
        Mock Invoke-Uninstaller {
            Add-UninstallerExitCode -ExitCode 42 -Description $Description
        }

        {
            Invoke-PowerToysCleanup -Confirm:$false
        } | Should Throw 'One or more PowerToys cleanup operations failed.'

        $script:failures.Count | Should Be 2
        $script:failures[0] | Should Match 'Could not validate'
        $script:failures[0] | Should Match 'MSI query failed'
        $script:failures[1] | Should Match 'exit code 42'
    }
}
