// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.PowerToys.UITest.Next;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.EnvironmentVariables.UITests;

[TestClass]
[TestCategory("EnvironmentVariables")]
public sealed class EnvironmentVariablesTests : UITestBase
{
    private static TestState state = null!;
    private EditorUi editor = null!;
    private string prefix = string.Empty;

    public EnvironmentVariablesTests()
        : base(PowerToysModule.PowerToysSettings, enableModules: ["EnvironmentVariables"])
    {
    }

    [ClassInitialize]
    public static void InitializeState(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        state = new TestState();
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void RestoreState() => state?.Dispose();

    [TestInitialize]
    public void OpenEditor()
    {
        state.Reset();
        prefix = $"PTUITest_{Guid.NewGuid():N}";
        LaunchEditor();
    }

    [TestCleanup]
    public async Task RestoreTestState()
    {
        try
        {
            await CaptureFailureArtifactsBeforeCleanupAsync();
            if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed && editor is not null)
            {
                string path = Path.Combine(TestContext.TestRunResultsDirectory!, $"{TestContext.TestName}-editor.json");
                File.WriteAllText(path, editor.Session.Inspect(depth: 30).GetRawText());
                TestContext.AddResultFile(path);
            }
        }
        finally
        {
            state.Reset();
        }
    }

    [TestMethod]
    public void StandardUserCannotEditSystemVariables()
    {
        Assert.IsFalse(editor.Session.IsElevated, "Launch as administrator OFF must launch a non-elevated editor.");
        Assert.IsTrue(editor.Session.Find<Button>(By.AccessibilityId("AddDefaultVariableUserBtn")).IsEnabled);
        Assert.IsFalse(editor.Session.Find<Button>(By.AccessibilityId("AddDefaultVariableSystemBtn")).IsEnabled);
        var system = editor.Expand("System");
        var path = editor.VariableCard(system, "Path");
        Assert.IsFalse(editor.Child<Button>(path, "Button", automationId: "VariableOptionsButton").IsEnabled, "System variables must be read-only.");
    }

    [TestMethod]
    public void UserVariableCrudUpdatesAppliedVariables()
    {
        string name = Track("User");
        editor.AddUserVariable(name, "original");
        editor.VariableMenu("User", name, "Edit");
        editor.Session.Find<TextBox>(By.AccessibilityId("EditVariableDialogValueTxtBox")).SetText("edited");
        editor.SaveDialog();
        EditorUi.AssertUserVariable(name, "edited");
        editor.AssertAppliedVariable(name, "edited");
        editor.VariableMenu("User", name, "Remove");
        editor.ConfirmRemoval();
        EditorUi.AssertUserVariable(name, null);
        editor.AssertAppliedVariable(name, null);
    }

    [TestMethod]
    public void ProfilesCanBeCreatedEditedSwitchedAndRemoved()
    {
        string first = prefix + "_Profile1";
        string second = prefix + "_Profile2";
        string one = Track("One");
        string two = Track("Two");
        string three = Track("Three");

        editor.CreateProfile(first, false);
        Assert.AreEqual(0, EditorUi.ReadProfiles().EnumerateArray().Single().GetProperty("Variables").GetArrayLength(), "The first profile should initially be empty.");
        editor.EditProfile(first, (one, "first"));
        editor.CreateProfile(second, true, (two, "second"), (three, "third"));
        EditorUi.AssertUserVariable(two, "second");
        EditorUi.AssertUserVariable(three, "third");
        editor.AssertAppliedVariable(two, "second");
        editor.AssertAppliedVariable(three, "third");

        editor.SetProfileEnabled(first, true);
        editor.AssertProfile(second, false);
        Assert.IsFalse(editor.Child<ToggleSwitch>(editor.Expander(second), "ToggleSwitch").IsOn, "Applying a profile must turn off the previously active profile's switch.");
        EditorUi.AssertUserVariable(one, "first");
        EditorUi.AssertUserVariable(two, null);
        EditorUi.AssertUserVariable(three, null);
        editor.AssertAppliedVariable(one, "first");
        editor.AssertAppliedVariable(two, null);
        editor.AssertAppliedVariable(three, null);

        editor.SetProfileEnabled(first, false);
        EditorUi.AssertUserVariable(one, null);
        editor.AssertAppliedVariable(one, null);
        editor.RemoveProfile(first);
        editor.RemoveProfile(second);
        Assert.AreEqual(0, EditorUi.ReadProfiles().GetArrayLength(), "Both profiles must be gone from profiles.json.");
    }

    [TestMethod]
    public void ExistingVariableOverrideCreatesAndRestoresBackup()
    {
        string name = Track("Override");
        string profile = prefix + "_OverrideProfile";
        string backup = name + "_PowerToys_" + profile;
        state.TrackUserVariable(backup);
        editor.AddUserVariable(name, "original");
        editor.CreateProfile(profile, false, (name, "overridden"));
        editor.SetProfileEnabled(profile, true);
        EditorUi.AssertUserVariable(name, "overridden");
        EditorUi.AssertUserVariable(backup, "original");
        editor.AssertAppliedVariable(name, "overridden");
        editor.AssertAppliedVariable(backup, "original");
        editor.SetProfileEnabled(profile, false);
        EditorUi.AssertUserVariable(name, "original");
        EditorUi.AssertUserVariable(backup, null);
        editor.AssertAppliedVariable(name, "original");
        editor.AssertAppliedVariable(backup, null);
        editor.RemoveProfile(profile);
    }

    [TestMethod]
    public void PathEntriesCanBeReorderedInsertedAndDeleted()
    {
        string profile = prefix + "_PathEditor";
        editor.CreateProfile(profile, false, ("PATH", "path1;path2;path3"));
        editor.AssertPathList(profile, "path1", "path2", "path3");
        editor.VariableMenu(profile, "PATH", "Edit");

        editor.PathEntryMenu(1, "Move up");
        editor.AssertPathEditor("path2;path1;path3");
        editor.PathEntryMenu(0, "Move down");
        editor.AssertPathEditor("path1;path2;path3");
        editor.PathEntryMenu(0, "Move up");
        editor.AssertPathEditor("path1;path2;path3");
        editor.PathEntryMenu(2, "Move down");
        editor.AssertPathEditor("path1;path2;path3");
        editor.PathEntryMenu(1, "Insert Before");
        editor.SetPathEntry(1, "before");
        editor.AssertPathEditor("path1;before;path2;path3");
        editor.PathEntryMenu(2, "Insert After");
        editor.SetPathEntry(3, "after");
        editor.AssertPathEditor("path1;before;path2;after;path3");
        editor.PathEntryMenu(0, "Delete");
        editor.AssertPathEditor("before;path2;after;path3");
        editor.SaveDialog();
        editor.AssertProfile(profile, false, ("PATH", "before;path2;after;path3"));
        editor.AssertPathList(profile, "before", "path2", "after", "path3");
        editor.RemoveProfile(profile);
    }

    [TestMethod]
    public void AppliedPathSurvivesReopenAndDeletingProfileRestoresUserPath()
    {
        string profile = prefix + "_PathProfile";
        string backup = "PATH_PowerToys_" + profile;
        state.TrackUserVariable("Path");
        state.TrackUserVariable(backup);
        string? original = TestState.ReadUserVariable("Path");
        string system = Environment.ExpandEnvironmentVariables(TestState.ReadSystemVariable("Path")!);
        editor.AssertAppliedVariable("Path", system + (original is null ? string.Empty : ";" + Environment.ExpandEnvironmentVariables(original)));
        editor.CreateProfile(profile, true, ("PATH", "path1;path2;path3"));
        EditorUi.AssertUserVariable("Path", "path1;path2;path3");
        EditorUi.AssertUserVariable(backup, original);
        editor.AssertAppliedVariable("Path", system + ";path1;path2;path3");

        editor.Step("Closing and reopening the editor to read persisted profile state");
        WindowControl.TryCloseByApp(TestState.ProcessName);
        EditorUi.Wait(() => !WindowsFinder.ListByApp(TestState.ProcessName).Any(), "The editor window did not close.");
        LaunchEditor();
        editor.AssertProfile(profile, true, ("PATH", "path1;path2;path3"));
        Assert.IsTrue(editor.Child<ToggleSwitch>(editor.Expander(profile), "ToggleSwitch").IsOn);
        EditorUi.AssertUserVariable("Path", "path1;path2;path3");
        editor.AssertAppliedVariable("Path", system + ";path1;path2;path3");

        editor.RemoveProfile(profile);
        EditorUi.AssertUserVariable("Path", original);
        EditorUi.AssertUserVariable(backup, null);
        editor.AssertAppliedVariable("Path", system + (original is null ? string.Empty : ";" + Environment.ExpandEnvironmentVariables(original)));
        editor.AssertAppliedVariable(backup, null);
    }

    private void LaunchEditor()
    {
        TestContext.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Launching Environment Variables from Settings with administrator mode OFF.");
        if (!Session.Has(By.AccessibilityId("EnvironmentVariablesNavItem"), 500))
        {
            Session.Find<NavigationViewItem>(By.AccessibilityId("AdvancedNavItem")).Click();
        }

        Session.Find<NavigationViewItem>(By.AccessibilityId("EnvironmentVariablesNavItem")).Click();
        var settings = new EditorUi(Session, TestContext);
        var adminCard = Session.Find<Element>(By.AccessibilityId("EnvironmentVariablesToggleLaunchAdministrator"));
        EditorUi.SetToggle(settings.Child<ToggleSwitch>(adminCard, "ToggleSwitch"), false);
        Assert.IsTrue(
            WindowControl.WaitForForeground(new IntPtr(Session.WindowHandle), timeoutMS: 10_000),
            $"Settings must own foreground before clicking its launch card: {WindowControl.GetForegroundWindowInfo()}");
        Session.Find<Element>(By.AccessibilityId("EnvironmentVariablesLaunchButtonControl")).Click();
        var window = WindowsFinder.WaitForWindowByApp(TestState.ProcessName, candidate => candidate.Width > 400 && candidate.Height > 300, timeoutMS: 30_000);
        Assert.IsNotNull(window, "Environment Variables did not launch from Settings.");
        WindowHelper.SetWindowSize(new IntPtr(window.WindowHandle), WindowSize.Large);
        editor = new EditorUi(window, TestContext);
        Assert.IsTrue(window.WaitForElement(By.AccessibilityId("AddDefaultVariableUserBtn"), 15_000), "The editor did not become ready.");
    }

    private string Track(string suffix)
    {
        string name = prefix + "_" + suffix;
        state.TrackUserVariable(name);
        Assert.IsNull(TestState.ReadUserVariable(name), "The unique fixture name must not replace an existing variable.");
        return name;
    }
}
