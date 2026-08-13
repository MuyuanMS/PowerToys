param(
    [string]$TestDirectory = "x64\Debug\WinUI3Apps\FileConverterSmokeTest",
    [string]$InputFileName = "sample.bmp",
    [string]$ExpectedOutputFileName = "sample_converted.png",
    [string]$VerbName = "Convert to...",
    [int]$InvokeTimeoutMs = 20000,
    [int]$OutputWaitTimeoutMs = 10000,
    [switch]$UseDirectComFallback
)

$ErrorActionPreference = "Stop"

function Ensure-SmokeTestFixture([string]$Path)
{
    if (Test-Path -LiteralPath $Path)
    {
        return
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [byte[]]$bmp = @(
        0x42, 0x4D, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
        0x28, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
        0x13, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00
    )
    [System.IO.File]::WriteAllBytes($Path, $bmp)
}

$settingsPath = Join-Path $env:LOCALAPPDATA "Microsoft\PowerToys\settings.json"
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ($null -eq $settings.enabled -or $settings.enabled.FileConverter -ne $true)
{
    throw "File Converter must be enabled in PowerToys Settings before running this shell-verb smoke test."
}

$inputPath = Join-Path $TestDirectory $InputFileName
Ensure-SmokeTestFixture -Path $inputPath
$resolvedTestDir = (Resolve-Path $TestDirectory).Path
$outputPath = Join-Path $resolvedTestDir $ExpectedOutputFileName
if (Test-Path $outputPath)
{
    Remove-Item $outputPath -Force
}

$code = @"
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

public static class ShellVerbRunner
{
    public static string Invoke(string directoryPath, string fileName, string targetVerb, int timeoutMs)
    {
        string result = "Unknown";
        Exception error = null;
        bool completed = false;

        Thread thread = new Thread(() =>
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(shellType);
                object folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { directoryPath });
                if (folder == null)
                {
                    result = "Folder not found";
                    return;
                }

                Type folderType = folder.GetType();
                object item = folderType.InvokeMember("ParseName", BindingFlags.InvokeMethod, null, folder, new object[] { fileName });
                if (item == null)
                {
                    result = "Item not found";
                    return;
                }

                Type itemType = item.GetType();
                object verbs = itemType.InvokeMember("Verbs", BindingFlags.InvokeMethod, null, item, null);
                Type verbsType = verbs.GetType();
                int count = (int)verbsType.InvokeMember("Count", BindingFlags.GetProperty, null, verbs, null);

                for (int index = 0; index < count; index++)
                {
                    object verb = verbsType.InvokeMember("Item", BindingFlags.InvokeMethod, null, verbs, new object[] { index });
                    if (verb == null)
                    {
                        continue;
                    }

                    Type verbType = verb.GetType();
                    string name = (verbType.InvokeMember("Name", BindingFlags.GetProperty, null, verb, null) as string ?? string.Empty)
                        .Replace("&", string.Empty)
                        .Trim();

                    if (!string.Equals(name, targetVerb, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    verbType.InvokeMember("DoIt", BindingFlags.InvokeMethod, null, verb, null);
                    result = "Invoked";
                    return;
                }

                result = "Verb not found";
            }
            catch (Exception ex)
            {
                Exception current = ex;
                string details = string.Empty;
                while (current != null)
                {
                    details += current.GetType().FullName + ": " + current.Message + Environment.NewLine;
                    current = current.InnerException;
                }

                error = new Exception(details.Trim());
            }
            finally
            {
                completed = true;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(timeoutMs);

        if (!completed)
        {
            return "Timeout";
        }

        if (error != null)
        {
            return "Error: " + error.Message;
        }

        return result;
    }
}

[ComImport, Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(IShellItemArray psiItemArray, out IntPtr ppszName);
    [PreserveSig]
    int GetIcon(IShellItemArray psiItemArray, out IntPtr ppszIcon);
    [PreserveSig]
    int GetToolTip(IShellItemArray psiItemArray, out IntPtr ppszInfotip);
    [PreserveSig]
    int GetCanonicalName(out Guid pguidCommandName);
    [PreserveSig]
    int GetState(IShellItemArray psiItemArray, int fOkToBeSlow, out uint pCmdState);
    [PreserveSig]
    int Invoke(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.Interface)] object pbc);
    [PreserveSig]
    int GetFlags(out uint pFlags);
    [PreserveSig]
    int EnumSubCommands(out IEnumExplorerCommand ppEnum);
}

[ComImport, Guid("A88826F8-186F-4987-AADE-EA0CEF8FBFE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEnumExplorerCommand
{
    [PreserveSig]
    int Next(uint celt, out IExplorerCommand pUICommand, out uint pceltFetched);
    [PreserveSig]
    int Skip(uint celt);
    [PreserveSig]
    int Reset();
    [PreserveSig]
    int Clone(out IEnumExplorerCommand ppenum);
}

[ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemArray
{
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItem
{
}

public static class FileConverterExplorerCommandRunner
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromShellItem(IShellItem psi, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    private static string NormalizeLabel(string value)
    {
        return (value ?? string.Empty).Replace("&", string.Empty).Trim();
    }

    public static string InvokeBySubCommand(string inputFilePath, string targetSubCommandLabel, int timeoutMs)
    {
        string result = "Unknown";
        Exception error = null;
        bool completed = false;

        Thread thread = new Thread(() =>
        {
            try
            {
                Guid shellItemGuid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                int hr = SHCreateItemFromParsingName(inputFilePath, IntPtr.Zero, ref shellItemGuid, out IShellItem shellItem);
                if (hr < 0)
                {
                    result = "SHCreateItemFromParsingName failed: 0x" + hr.ToString("X8");
                    return;
                }

                Guid shellArrayGuid = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
                hr = SHCreateShellItemArrayFromShellItem(shellItem, ref shellArrayGuid, out IShellItemArray selection);
                if (hr < 0)
                {
                    result = "SHCreateShellItemArrayFromShellItem failed: 0x" + hr.ToString("X8");
                    return;
                }

                Type commandType = Type.GetTypeFromCLSID(new Guid("57EC18F5-24D5-4DC6-AE2E-9D0F7A39F8BA"), true);
                IExplorerCommand root = (IExplorerCommand)Activator.CreateInstance(commandType);

                hr = root.EnumSubCommands(out IEnumExplorerCommand enumCommands);
                if (hr < 0 || enumCommands == null)
                {
                    result = "EnumSubCommands failed: 0x" + hr.ToString("X8");
                    return;
                }

                string expected = NormalizeLabel(targetSubCommandLabel);
                bool requireMatch = !string.IsNullOrWhiteSpace(expected);

                while (true)
                {
                    hr = enumCommands.Next(1, out IExplorerCommand command, out uint fetched);
                    if (fetched == 0 || command == null)
                    {
                        result = "Subcommand not found";
                        return;
                    }

                    IntPtr titlePtr = IntPtr.Zero;
                    string title = string.Empty;
                    int titleHr = command.GetTitle(selection, out titlePtr);
                    if (titleHr >= 0 && titlePtr != IntPtr.Zero)
                    {
                        title = Marshal.PtrToStringUni(titlePtr) ?? string.Empty;
                        CoTaskMemFree(titlePtr);
                    }

                    string normalizedTitle = NormalizeLabel(title);
                    if (requireMatch && !string.Equals(normalizedTitle, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hr = command.Invoke(selection, null);
                    result = hr < 0 ? ("Invoke failed: 0x" + hr.ToString("X8")) : "Invoked";
                    return;
                }
            }
            catch (Exception ex)
            {
                Exception current = ex;
                string details = string.Empty;
                while (current != null)
                {
                    details += current.GetType().FullName + ": " + current.Message + Environment.NewLine;
                    current = current.InnerException;
                }

                error = new Exception(details.Trim());
            }
            finally
            {
                completed = true;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(timeoutMs);

        if (!completed)
        {
            return "Timeout";
        }

        if (error != null)
        {
            return "Error: " + error.Message;
        }

        return result;
    }
}
"@

Add-Type -TypeDefinition $code -Language CSharp
function Resolve-TargetSubCommandLabel([string]$ExpectedOutputName, [string]$RequestedVerb)
{
    if (-not [string]::IsNullOrWhiteSpace($RequestedVerb) -and $RequestedVerb -ne "Convert to...")
    {
        return $RequestedVerb
    }

    $extension = [System.IO.Path]::GetExtension($ExpectedOutputName).ToLowerInvariant()
    switch ($extension)
    {
        ".png" { return "PNG" }
        ".jpg" { return "JPG" }
        ".jpeg" { return "JPEG" }
        ".bmp" { return "BMP" }
        ".tif" { return "TIFF" }
        ".tiff" { return "TIFF" }
        ".heic" { return "HEIC" }
        ".heif" { return "HEIF" }
        ".webp" { return "WebP" }
        default { return "PNG" }
    }
}

$invokeResult = [ShellVerbRunner]::Invoke($resolvedTestDir, $InputFileName, $VerbName, $InvokeTimeoutMs)
Write-Host "Invoke result: $invokeResult"

if ($invokeResult -eq "Verb not found")
{
    if (-not $UseDirectComFallback)
    {
        throw "The Explorer-hosted verb was not found. Re-run with -UseDirectComFallback to restart the Debug Runner in authenticated test-client mode before direct COM activation."
    }

    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
    $runnerPath = Join-Path $repoRoot "x64\Debug\PowerToys.exe"
    if (-not (Test-Path -LiteralPath $runnerPath))
    {
        throw "Debug Runner not found at: $runnerPath"
    }

    Get-Process PowerToys -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    $env:POWERTOYS_FILECONVERTER_TEST_CLIENT_DIR = Split-Path -Parent (Get-Process -Id $PID).Path
    $runner = Start-Process -FilePath $runnerPath -PassThru
    Start-Sleep -Seconds 1
    if ($runner.HasExited)
    {
        throw "Debug Runner exited before direct COM activation."
    }

    $inputPath = Join-Path $resolvedTestDir $InputFileName
    $subCommandLabel = Resolve-TargetSubCommandLabel -ExpectedOutputName $ExpectedOutputFileName -RequestedVerb $VerbName
    Write-Host "Shell verb fallback: trying IExplorerCommand subcommand '$subCommandLabel'"
    $invokeResult = [FileConverterExplorerCommandRunner]::InvokeBySubCommand($inputPath, $subCommandLabel, $InvokeTimeoutMs)
    Write-Host "Fallback invoke result: $invokeResult"
}

if ($invokeResult -ne "Invoked")
{
    throw "Verb invocation failed: $invokeResult"
}

$waited = 0
$step = 250
while ($waited -lt $OutputWaitTimeoutMs -and -not (Test-Path $outputPath))
{
    Start-Sleep -Milliseconds $step
    $waited += $step
}

if (-not (Test-Path $outputPath))
{
    throw "Output file was not created: $outputPath"
}

$item = Get-Item $outputPath
Write-Host "Created: $($item.FullName)"
Write-Host "Size: $($item.Length)"
