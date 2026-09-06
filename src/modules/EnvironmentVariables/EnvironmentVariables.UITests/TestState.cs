// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.PowerToys.UITest.Next;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace Microsoft.EnvironmentVariables.UITests;

internal sealed class TestState : IDisposable
{
    internal const string ProcessName = "PowerToys.EnvironmentVariables";

    internal static readonly string ModuleDirectory = Path.Combine(SettingsConfigHelper.PowerToysSettingsRoot, "EnvironmentVariables");
    internal static readonly string ProfilesPath = Path.Combine(ModuleDirectory, "profiles.json");

    private readonly Dictionary<string, (object? Value, RegistryValueKind Kind)> userVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]?> files = new();

    internal TestState()
    {
        StopEditor();
        Directory.CreateDirectory(ModuleDirectory);
        foreach (string name in new[] { "profiles.json", "settings.json" })
        {
            string path = Path.Combine(ModuleDirectory, name);
            files.Add(path, File.Exists(path) ? File.ReadAllBytes(path) : null);
        }
    }

    internal static string? ReadUserVariable(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    internal static string? ReadSystemVariable(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    internal static void StopEditor()
    {
        Assert.IsTrue(
            WindowControl.TryKillProcessTreeByNameAndWait(ProcessName, timeoutMS: 10_000),
            "Environment Variables must exit before restoring its files and registry values.");
    }

    internal void TrackUserVariable(string name)
    {
        if (userVariables.ContainsKey(name))
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        object? value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        userVariables.Add(name, (value, value is null ? RegistryValueKind.String : key!.GetValueKind(name)));
    }

    internal void Reset()
    {
        StopEditor();
        using var key = Registry.CurrentUser.CreateSubKey("Environment", writable: true);
        foreach (var (name, original) in userVariables)
        {
            if (original.Value is null)
            {
                Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
            }
            else
            {
                // Notify Explorer through the OS API, then preserve the original registry kind
                // as well as the unexpanded text (notably REG_EXPAND_SZ User PATH values).
                Environment.SetEnvironmentVariable(name, original.Value.ToString(), EnvironmentVariableTarget.User);
                key.SetValue(name, original.Value, original.Kind);
            }
        }

        userVariables.Clear();
        File.WriteAllText(ProfilesPath, "[]");
    }

    public void Dispose()
    {
        try
        {
            Reset();
        }
        finally
        {
            foreach (var (path, original) in files)
            {
                if (original is null)
                {
                    File.Delete(path);
                }
                else
                {
                    File.WriteAllBytes(path, original);
                }
            }
        }
    }
}
