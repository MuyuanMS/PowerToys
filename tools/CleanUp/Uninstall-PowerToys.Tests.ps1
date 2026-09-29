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

        Mock Get-SpecialFolderPath {
            switch ($Folder) {
                'CommonApplicationData' { return Join-Path $TestDrive 'ApiProgramData' }
                'LocalApplicationData' { return Join-Path $TestDrive 'ApiLocalAppData' }
                'ProgramFiles' { return Join-Path $TestDrive 'ApiProgramFiles' }
                'ProgramFilesX86' { return Join-Path $TestDrive 'ApiProgramFilesX86' }
                default { throw "Unexpected special folder: $Folder" }
            }
        }
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
        Assert-MockCalled Invoke-Uninstaller -Times 1 -ParameterFilter {
            $FilePath -eq (Join-Path ([Environment]::SystemDirectory) 'msiexec.exe')
        }
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

    It 'closes and releases every MSI database COM object after reading a property' {
        $script:viewClosed = $false
        $record = [pscustomobject]@{}
        $record | Add-Member -MemberType ScriptMethod -Name StringData -Value { param($index) 'PropertyValue' }
        $view = [pscustomobject]@{}
        $view | Add-Member -MemberType ScriptMethod -Name Execute -Value {}
        $view | Add-Member -MemberType ScriptMethod -Name Fetch -Value { $record }.GetNewClosure()
        $view | Add-Member -MemberType ScriptMethod -Name Close -Value { $script:viewClosed = $true }
        $database = [pscustomobject]@{}
        $database | Add-Member -MemberType ScriptMethod -Name OpenView -Value { param($query) $view }.GetNewClosure()
        $installer = [pscustomobject]@{}
        $installer | Add-Member -MemberType ScriptMethod -Name OpenDatabase -Value {
            param($path, $mode)
            $database
        }.GetNewClosure()

        $script:fakeWindowsInstaller = $installer
        Mock New-WindowsInstallerObject { $script:fakeWindowsInstaller }
        Mock Release-ComObjectSafely {}

        Get-MsiDatabaseProperty -Path (Join-Path $TestDrive 'staged.msi') -Property 'ProductCode' |
            Should Be 'PropertyValue'

        $script:viewClosed | Should Be $true
        Assert-MockCalled Release-ComObjectSafely -Times 1 -Scope It -ParameterFilter {
            $ComObject -eq $record
        }
        Assert-MockCalled Release-ComObjectSafely -Times 1 -Scope It -ParameterFilter {
            $ComObject -eq $view
        }
        Assert-MockCalled Release-ComObjectSafely -Times 1 -Scope It -ParameterFilter {
            $ComObject -eq $database
        }
        Assert-MockCalled Release-ComObjectSafely -Times 1 -Scope It -ParameterFilter {
            $ComObject -eq $installer
        }
    }

    It 'closes and releases opened MSI COM objects when property execution fails' {
        $script:viewClosed = $false
        $view = [pscustomobject]@{}
        $view | Add-Member -MemberType ScriptMethod -Name Execute -Value { throw 'MSI query failed.' }
        $view | Add-Member -MemberType ScriptMethod -Name Close -Value { $script:viewClosed = $true }
        $database = [pscustomobject]@{}
        $database | Add-Member -MemberType ScriptMethod -Name OpenView -Value { param($query) $view }.GetNewClosure()
        $installer = [pscustomobject]@{}
        $installer | Add-Member -MemberType ScriptMethod -Name OpenDatabase -Value {
            param($path, $mode)
            $database
        }.GetNewClosure()

        $script:fakeWindowsInstaller = $installer
        Mock New-WindowsInstallerObject { $script:fakeWindowsInstaller }
        Mock Release-ComObjectSafely {}

        {
            Get-MsiDatabaseProperty -Path (Join-Path $TestDrive 'staged.msi') -Property 'ProductCode'
        } | Should Throw 'MSI query failed.'

        $script:viewClosed | Should Be $true
        Assert-MockCalled Release-ComObjectSafely -Times 3 -Scope It
        Assert-MockCalled Release-ComObjectSafely -Times 1 -Scope It -ParameterFilter {
            $ComObject -eq $view
        }
        Assert-MockCalled Release-ComObjectSafely -Times 1 -Scope It -ParameterFilter {
            $ComObject -eq $database
        }
        Assert-MockCalled Release-ComObjectSafely -Times 1 -Scope It -ParameterFilter {
            $ComObject -eq $installer
        }
    }

    It 'removes directory reparse points without enumerating their targets' {
        $root = Join-Path $TestDrive 'root'
        $normalDirectory = Join-Path $root 'normal'
        $file = Join-Path $normalDirectory 'file.txt'
        $junction = Join-Path $root 'junction'
        $provider = [pscustomobject]@{ Name = 'FileSystem' }
        $items = @{
            $root = [pscustomobject]@{
                FullName = $root
                PSIsContainer = $true
                Attributes = [IO.FileAttributes]::Directory
                PSProvider = $provider
            }
            $normalDirectory = [pscustomobject]@{
                FullName = $normalDirectory
                PSIsContainer = $true
                Attributes = [IO.FileAttributes]::Directory
                PSProvider = $provider
            }
            $file = [pscustomobject]@{
                FullName = $file
                PSIsContainer = $false
                Attributes = [IO.FileAttributes]::Normal
                PSProvider = $provider
            }
            $junction = [pscustomobject]@{
                FullName = $junction
                PSIsContainer = $true
                Attributes = [IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint
                PSProvider = $provider
            }
        }

        Mock Get-Item { $items[$LiteralPath] }
        Mock Get-ChildItem {
            if ($LiteralPath -eq $root) {
                return @($items[$normalDirectory], $items[$junction])
            }

            if ($LiteralPath -eq $normalDirectory) {
                return $items[$file]
            }

            throw "Unexpected enumeration of $LiteralPath"
        }
        Mock Remove-Item {}

        Remove-FileSystemTreeWithoutFollowingReparsePoints -Path $root

        Assert-MockCalled Get-ChildItem -Times 1 -Scope It -ParameterFilter {
            $LiteralPath -eq $root
        }
        Assert-MockCalled Get-ChildItem -Times 1 -Scope It -ParameterFilter {
            $LiteralPath -eq $normalDirectory
        }
        Assert-MockCalled Get-ChildItem -Times 0 -Scope It -ParameterFilter {
            $LiteralPath -eq $junction
        }
        Assert-MockCalled Remove-Item -Times 4 -Scope It -ParameterFilter {
            -not $Recurse
        }
    }

    It 'defers a per-user bundle until the machine-wide installation is removed' {
        $script:productCall = 0
        Mock Get-PowerToysMsiProducts {
            $script:productCall++
            if ($script:productCall -eq 1) {
                return [pscustomobject]@{
                    Scope = 'PerMachine'
                    ProductCode = '{55555555-5555-5555-5555-555555555555}'
                    UpgradeCode = '{EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE}'
                    State = 5
                    StateName = 'Default'
                }
            }

            return @()
        }
        $script:bundleCall = 0
        Mock Get-PowerToysBundles {
            $script:bundleCall++
            if ($script:bundleCall -eq 1) {
                return [pscustomobject]@{
                    Scope = 'PerUser'
                    DisplayName = 'PowerToys (Preview) x64'
                    DisplayVersion = '0.100.2'
                    RegistryPath = 'TestRegistryPath'
                }
            }

            return @()
        }
        Mock Test-IsAdministrator { $false }
        Mock Get-BundleExecutable { throw 'The deferred bundle must not be inspected.' }
        Mock Invoke-Uninstaller { throw 'The deferred bundle must not be launched.' }

        {
            Invoke-PowerToysCleanup -Confirm:$false
        } | Should Throw 'One or more PowerToys cleanup operations failed.'

        $script:failures.Count | Should Be 1
        $script:failures[0] | Should Match 'Deferred the per-user bundle'
        Assert-MockCalled Get-BundleExecutable -Times 0 -Scope It
        Assert-MockCalled Invoke-Uninstaller -Times 0 -Scope It
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

    It 'aggregates a second MSI lookup failure and continues processing' {
        $script:productCall = 0
        Mock Get-PowerToysMsiProducts {
            $script:productCall++
            if ($script:productCall -eq 1) {
                return @(
                    [pscustomobject]@{
                        Scope = 'PerUser'
                        ProductCode = '{66666666-6666-6666-6666-666666666666}'
                        UpgradeCode = '{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}'
                        State = 5
                        StateName = 'Default'
                    },
                    [pscustomobject]@{
                        Scope = 'PerUser'
                        ProductCode = '{77777777-7777-7777-7777-777777777777}'
                        UpgradeCode = '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}'
                        State = 5
                        StateName = 'Default'
                    })
            }

            return @()
        }
        Mock Get-PowerToysBundles { @() }
        Mock Test-IsAdministrator { $false }
        Mock Test-PowerToysMsiProduct { $true }
        Mock Get-MsiProductProperty {
            if ($ProductCode -eq '{66666666-6666-6666-6666-666666666666}') {
                throw 'Second LocalPackage lookup failed.'
            }

            return (Join-Path $TestDrive 'cached.msi')
        }
        Mock Copy-MsiForExecution { Join-Path $TestDrive 'staged.msi' }
        Mock Invoke-Uninstaller {
            Add-UninstallerExitCode -ExitCode 42 -Description $Description
        }

        {
            Invoke-PowerToysCleanup -Confirm:$false
        } | Should Throw 'One or more PowerToys cleanup operations failed.'

        $script:failures.Count | Should Be 2
        $script:failures[0] | Should Match 'Could not stage the cached MSI'
        $script:failures[0] | Should Match 'Second LocalPackage lookup failed'
        $script:failures[1] | Should Match 'exit code 42'
        Assert-MockCalled Invoke-Uninstaller -Times 1 -Scope It
    }

    It 'does not recursively remove profile-owned paths from an elevated pass' {
        Mock Get-PowerToysMsiProducts { @() }
        Mock Get-PowerToysBundles { @() }
        Mock Test-IsAdministrator { $true }

        {
            Invoke-PowerToysCleanup -RemoveSettings -Confirm:$false
        } | Should Throw 'One or more PowerToys cleanup operations failed.'

        $script:failures.Count | Should Be 1
        $script:failures[0] | Should Match 'Rerun with -RemoveSettings from a non-elevated PowerShell window'
        Assert-MockCalled Remove-KnownArtifact -Times 0 -Scope It -ParameterFilter {
            $Path -like "$(Join-Path $TestDrive 'ApiLocalAppData')*"
        }
        Assert-MockCalled Remove-KnownArtifact -Times 0 -Scope It -ParameterFilter {
            $Path -eq 'Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\PowerToys'
        }
    }

    It 'uses API-backed machine roots despite process environment overrides' {
        $env:ProgramData = Join-Path $TestDrive 'PoisonedProgramData'
        $env:ProgramFiles = Join-Path $TestDrive 'PoisonedProgramFiles'
        ${env:ProgramFiles(x86)} = Join-Path $TestDrive 'PoisonedProgramFilesX86'
        Mock Get-PowerToysMsiProducts { @() }
        Mock Get-PowerToysBundles { @() }
        Mock Test-IsAdministrator { $true }

        Invoke-PowerToysCleanup -Confirm:$false

        Assert-MockCalled New-ProtectedDirectory -Times 2 -Scope It -ParameterFilter {
            $AdministratorOnly -and $Path -like "$(Join-Path $TestDrive 'ApiProgramData')*"
        }
        Assert-MockCalled Remove-KnownArtifact -Times 1 -Scope It -ParameterFilter {
            $Path -eq (Join-Path (Join-Path $TestDrive 'ApiProgramFiles') 'PowerToys')
        }
        Assert-MockCalled Remove-KnownArtifact -Times 1 -Scope It -ParameterFilter {
            $Path -eq (Join-Path (Join-Path $TestDrive 'ApiProgramFilesX86') 'PowerToys')
        }
        Assert-MockCalled New-ProtectedDirectory -Times 0 -Scope It -ParameterFilter {
            $Path -like "$env:ProgramData*"
        }
        Assert-MockCalled Remove-KnownArtifact -Times 0 -Scope It -ParameterFilter {
            $Path -like "$env:ProgramFiles*" -or $Path -like "${env:ProgramFiles(x86)}*"
        }
    }

    It 'removes the current user PowerToys settings key from a non-elevated settings pass' {
        Mock Get-PowerToysMsiProducts { @() }
        Mock Get-PowerToysBundles { @() }
        Mock Test-IsAdministrator { $false }

        Invoke-PowerToysCleanup -RemoveSettings -Confirm:$false

        Assert-MockCalled Remove-KnownArtifact -Times 1 -Scope It -ParameterFilter {
            $Path -eq 'Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\PowerToys'
        }
        Assert-MockCalled Remove-KnownArtifact -Times 1 -Scope It -ParameterFilter {
            $Path -eq (Join-Path (Join-Path $TestDrive 'ApiLocalAppData') 'Microsoft\PowerToys')
        }
    }
}
