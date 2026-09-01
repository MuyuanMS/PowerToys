// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.PowerToys.UITest.Next;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MouseUtils.UITests;

[TestClass]
public class AutoHideCursorTests : UITestBase
{
    private const string ModuleName = "AutoHideCursor";
    private const string WorkerProcessName = "PowerToys.AutoHideCursor";
    private const string ModuleToggleId = "MouseUtils_AutoHideCursorToggleId";
    private const string HideOnTypingToggleId = "MouseUtils_AutoHideCursorHideOnTypingToggleId";
    private const string HideOnIdleToggleId = "MouseUtils_AutoHideCursorHideOnIdleToggleId";
    private const string IdleDelayId = "MouseUtils_AutoHideCursorIdleDelayId";
    private static readonly IDisposable ModuleSettings = SettingsConfigHelper.PreserveModuleSettings(ModuleName);

    public AutoHideCursorTests()
        : base(PowerToysModule.PowerToysSettings, enableModules: new[] { ModuleName })
    {
    }

    [ClassCleanup]
    public static void RestoreModuleSettings() => ModuleSettings.Dispose();

    protected override void PrepareTestState()
    {
        MouseUtilsTestHelper.ReplaceModuleSettings(ModuleName, """
            {
              "name": "AutoHideCursor",
              "version": "1.0",
              "properties": {
                "hide_on_typing": { "value": true },
                "hide_on_idle": { "value": false },
                "idle_delay_ms": { "value": 120000 }
              }
            }
            """);
    }

    [TestCleanup]
    public async Task CaptureFailureArtifacts()
    {
        await CaptureFailureArtifactsBeforeCleanupAsync();
    }

    [TestMethod]
    [TestCategory("MouseUtils")]
    [TestCategory("AutoHideCursor")]
    public void ModuleEnablementControlsWorkerLifecycle()
    {
        MouseUtilsTestHelper.NavigateToMouseUtilities(this);
        MouseUtilsTestHelper.SetModuleEnabled(this, ModuleToggleId, true);
        AssertWorkerRunning(true, "The worker did not start after enabling Auto Hide Cursor.");

        MouseUtilsTestHelper.SetModuleEnabled(this, ModuleToggleId, false);
        AssertWorkerRunning(false, "The worker remained running after disabling Auto Hide Cursor.");

        MouseUtilsTestHelper.SetModuleEnabled(this, ModuleToggleId, true);
        AssertWorkerRunning(true, "The worker did not restart after re-enabling Auto Hide Cursor.");
    }

    [TestMethod]
    [TestCategory("MouseUtils")]
    [TestCategory("AutoHideCursor")]
    public void TriggerBindingsAndDelayBoundsAreApplied()
    {
        MouseUtilsTestHelper.NavigateToMouseUtilities(this);
        MouseUtilsTestHelper.SetModuleEnabled(this, ModuleToggleId, true);

        var hideOnTyping = Session.Find<ToggleSwitch>(By.AccessibilityId(HideOnTypingToggleId), 10_000);
        var hideOnIdle = Session.Find<ToggleSwitch>(By.AccessibilityId(HideOnIdleToggleId), 10_000);
        Assert.IsTrue(hideOnTyping.IsOn, "Hide-on-typing should reflect the seeded setting.");
        Assert.IsFalse(hideOnIdle.IsOn, "Hide-on-idle should reflect the seeded setting.");

        hideOnTyping.Toggle(false);
        Assert.IsTrue(hideOnTyping.WaitForProperty("ToggleState", "Off", 5_000));
        hideOnIdle.Toggle(true);
        Assert.IsTrue(hideOnIdle.WaitForProperty("ToggleState", "On", 5_000));
        AssertSettingsPersisted(hideOnTyping: false, hideOnIdle: true);

        var idleDelay = Session.Find<Element>(By.AccessibilityId(IdleDelayId), 10_000);
        Assert.IsTrue(idleDelay.IsEnabled, "Idle delay should be enabled when idle hiding is selected.");
        Assert.AreEqual("1", idleDelay.GetProperty("Minimum"), "The idle delay minimum should be one second.");
        Assert.AreEqual("60", idleDelay.GetProperty("Maximum"), "The idle delay maximum should be sixty seconds.");
        Assert.AreEqual("60", idleDelay.GetValue(), "The out-of-range seeded delay should be clamped to sixty seconds.");

        hideOnIdle.Toggle(false);
        Assert.IsTrue(hideOnIdle.WaitForProperty("ToggleState", "Off", 5_000));
        idleDelay = Session.Find<Element>(By.AccessibilityId(IdleDelayId), 10_000);
        Assert.IsFalse(idleDelay.IsEnabled, "Idle delay should be disabled when idle hiding is not selected.");
        AssertSettingsPersisted(hideOnTyping: false, hideOnIdle: false);
    }

    private static void AssertWorkerRunning(bool expected, string message)
    {
        var result = WaitHelper.WaitForStable(
            IsWorkerRunning,
            running => running == expected,
            10_000,
            requiredConsecutiveMatches: 2);
        Assert.IsTrue(result.Succeeded, message);
    }

    private static bool IsWorkerRunning()
    {
        var processes = Process.GetProcessesByName(WorkerProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void AssertSettingsPersisted(bool hideOnTyping, bool hideOnIdle)
    {
        var settingsPath = Path.Combine(SettingsConfigHelper.PowerToysSettingsRoot, ModuleName, "settings.json");
        var result = WaitHelper.WaitForStable(
            () => ReadTriggerSettings(settingsPath),
            settings => settings == (hideOnTyping, hideOnIdle),
            5_000);
        Assert.IsTrue(
            result.Succeeded,
            $"Trigger settings were not persisted as hide_on_typing={hideOnTyping}, hide_on_idle={hideOnIdle}.");
    }

    private static (bool HideOnTyping, bool HideOnIdle)? ReadTriggerSettings(string settingsPath)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(settingsPath));
            return (
                root?["properties"]?["hide_on_typing"]?["value"]?.GetValue<bool>() ?? false,
                root?["properties"]?["hide_on_idle"]?["value"]?.GetValue<bool>() ?? false);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
