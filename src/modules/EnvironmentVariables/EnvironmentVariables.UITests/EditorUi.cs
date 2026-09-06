// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Microsoft.PowerToys.UITest.Next;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.EnvironmentVariables.UITests;

internal sealed class EditorUi(Session session, TestContext context)
{
    internal Session Session { get; } = session;

    internal void Step(string text) => context.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {text}");

    internal static IEnumerable<JsonElement> Nodes(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("type", out _))
            {
                yield return root;
            }

            foreach (var property in root.EnumerateObject())
            {
                foreach (var child in Nodes(property.Value))
                {
                    yield return child;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                foreach (var child in Nodes(item))
                {
                    yield return child;
                }
            }
        }
    }

    internal static string Property(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    internal JsonElement Inspect(Element parent) =>
        WinappCli.InvokeJson("ui", "inspect", parent.Selector, Session.TargetFlag, Session.TargetValue, "--json", "-d", "30");

    internal T Child<T>(Element parent, string className, string? name = null, string? automationId = null)
        where T : Element, new()
    {
        // Element.Find currently searches the whole session; inspect the actual subtree to avoid
        // confusing User, System, profile, and Applied rows that share the same variable name.
        var tree = Inspect(parent);
        var matches = Nodes(tree).Where(node =>
            Property(node, "className") == className &&
            (name is null || Property(node, "name") == name) &&
            (automationId is null || Property(node, "automationId") == automationId)).ToArray();
        Assert.HasCount(1, matches, $"Expected one {className} '{name ?? automationId}' below {parent.Selector}. Tree: {tree}");
        return Resolve<T>(matches[0]);
    }

    private T Resolve<T>(JsonElement node)
        where T : Element, new()
    {
        string selector = Property(node, "selector");
        Assert.IsFalse(string.IsNullOrEmpty(selector), $"UIA node has no selector: {node}");
        return Session.Find<T>(By.Slug(selector));
    }

    internal void PathEntryMenu(int index, string action)
    {
        Step($"{action} PATH list entry {index}");
        var buttons = Session.FindAll<Button>(By.AccessibilityId("PathEntryOptionsButton")).ToArray();
        Assert.IsTrue(index >= 0 && index < buttons.Length, $"PATH entry {index} was not found among {buttons.Length} entries.");
        buttons[index].Invoke();
        Menu(action);
    }

    internal void SetPathEntry(int index, string value)
    {
        Step($"Setting PATH list entry {index} to {value}");
        var entries = Nodes(Session.Inspect(depth: 30)).Where(node =>
            Property(node, "className") == "TextBox" && Property(node, "automationId") == string.Empty).ToArray();
        Assert.IsTrue(index >= 0 && index < entries.Length, $"PATH entry {index} was not found.");
        Resolve<TextBox>(entries[index]).SetText(value);
        Session.Find<TextBox>(By.AccessibilityId("EditVariableDialogNameTxtBox")).Focus();
    }

    internal void AssertPathEditor(string expected)
    {
        Assert.IsTrue(
            Session.Find<TextBox>(By.AccessibilityId("EditVariableDialogValueTxtBox")).WaitForValue(expected, timeoutMS: 10_000),
            $"PATH list editing did not produce '{expected}'.");
    }

    internal void AssertPathList(string profile, params string[] expected)
    {
        var card = VariableCard(Expand(profile), "PATH");
        var text = Nodes(Inspect(card)).Where(node => Property(node, "type") == "Text")
            .Select(node => Property(node, "name")).Where(name => name != "PATH").ToArray();
        CollectionAssert.AreEqual(expected, text, "PATH must be displayed as separate ordered entries, not a semicolon-delimited value.");
    }

    internal Element Expander(string name)
    {
        var matches = Session.FindAll<Element>(By.Name(name), 10_000)
            .Where(element => element.ClassName == "SettingsExpander" && element.Name == name).ToArray();
        Assert.HasCount(1, matches, $"Expected the '{name}' variables expander.");
        return matches[0];
    }

    internal Element Expand(string name)
    {
        var expander = Expander(name);
        var header = Child<Element>(expander, "Microsoft.UI.Xaml.Controls.Expander", name);
        if (header.GetProperty("ExpandCollapseState") != "Expanded")
        {
            Step($"Expanding {name}");
            header.Invoke();
            Assert.IsTrue(header.WaitForProperty("ExpandCollapseState", "Expanded", 10_000), $"'{name}' did not expand.");
        }

        return Expander(name);
    }

    internal Element VariableCard(Element parent, string name)
    {
        var tree = Inspect(parent);
        var matches = Nodes(tree).Where(node => Property(node, "className") == "SettingsCard" &&
            Nodes(node).Any(child => Property(child, "type") == "Text" && Property(child, "name") == name)).ToArray();
        Assert.HasCount(1, matches, $"Expected one variable card for {name}. Tree: {tree}");
        return Resolve<Element>(matches[0]);
    }

    internal void SaveDialog()
    {
        var save = Session.Find<Button>(By.AccessibilityId("PrimaryButton"));
        Assert.IsTrue(save.IsEnabled, "The dialog Save button should be enabled.");
        Step("Saving dialog");
        save.Invoke();
        Assert.IsTrue(save.WaitForGone(10_000), "The saved dialog did not close.");
    }

    internal void AddUserVariable(string name, string value)
    {
        Step($"Adding User variable {name}");
        Session.Find<Button>(By.AccessibilityId("AddDefaultVariableUserBtn")).Invoke();
        Session.Find<TextBox>(By.Name("Name")).SetText(name);
        Session.Find<TextBox>(By.Name("Value")).SetText(value);
        SaveDialog();
        AssertUserVariable(name, value);
        AssertAppliedVariable(name, value);
    }

    internal void VariableMenu(string set, string name, string action)
    {
        Step($"{action} variable {name} in {set}");
        var expander = Expand(set);
        var card = VariableCard(expander, name);
        Child<Button>(card, "Button", automationId: "VariableOptionsButton").Invoke();
        Menu(action);
    }

    internal void Menu(string name) =>
        Session.FindAll<Element>(By.Name(name)).Single(element => element.ControlType == "MenuItem" && element.Name == name).Invoke();

    internal void ConfirmRemoval()
    {
        Step("Confirming removal");
        var yes = Session.Find<Button>(By.AccessibilityId("PrimaryButton"));
        Assert.AreEqual("Yes", yes.Name);
        yes.Invoke();
        Assert.IsTrue(yes.WaitForGone(10_000), "The removal confirmation did not close.");
    }

    internal void CreateProfile(string name, bool enabled, params (string Name, string Value)[] variables)
    {
        Step($"Creating profile {name}");
        Session.Find<TextBlock>(By.Name("New profile")).Invoke();
        FillProfile(name, enabled, variables);
    }

    internal void EditProfile(string name, params (string Name, string Value)[] variables)
    {
        ProfileMenu(name, "Edit");
        FillProfile(name, false, variables);
    }

    internal void ProfileMenu(string name, string action)
    {
        Step($"{action} profile {name}");
        Child<Button>(Expander(name), "Button", automationId: "ProfileOptionsButton").Invoke();
        Menu(action);
    }

    private void FillProfile(string name, bool enabled, (string Name, string Value)[] variables)
    {
        Session.Find<TextBox>(By.Name("Name")).SetText(name);
        foreach (var variable in variables)
        {
            Step($"Adding {variable.Name} to profile {name}");
            Session.Find<TextBlock>(By.Name("Add variable")).Invoke();
            Session.Find<TextBox>(By.AccessibilityId("AddNewVariableName")).SetText(variable.Name);
            Session.Find<TextBox>(By.AccessibilityId("AddNewVariableValue")).SetText(variable.Value);
            var add = Session.Find<Button>(By.AccessibilityId("ConfirmAddVariableBtn"));
            Assert.IsTrue(add.IsEnabled, $"Could not add {variable.Name} to {name}.");
            add.Invoke();
            Assert.IsTrue(add.WaitForGone(10_000), "The Add variable flyout did not close.");
        }

        var toggle = Session.Find<ToggleSwitch>(By.Name("Enabled"));
        SetToggle(toggle, enabled);
        SaveDialog();
        AssertProfile(name, enabled, variables);
    }

    internal void SetProfileEnabled(string name, bool enabled)
    {
        Step($"Setting profile {name} enabled={enabled}");
        SetToggle(Child<ToggleSwitch>(Expander(name), "ToggleSwitch"), enabled);
        AssertProfile(name, enabled);
    }

    internal static void SetToggle(ToggleSwitch toggle, bool enabled)
    {
        if (toggle.IsOn != enabled)
        {
            toggle.Invoke();
        }

        Assert.IsTrue(toggle.WaitForProperty("ToggleState", enabled ? "On" : "Off", 10_000), "The toggle state did not update.");
    }

    internal void RemoveProfile(string name)
    {
        ProfileMenu(name, "Remove");
        ConfirmRemoval();
        Wait(
            () => !ReadProfiles().EnumerateArray().Any(profile => profile.GetProperty("Name").GetString() == name),
            $"Profile {name} was not removed from profiles.json.");
        Assert.IsFalse(Session.FindAll<Element>(By.Name(name), 0).Any(element => element.ClassName == "SettingsExpander"), $"Profile {name} remains in the UI.");
    }

    internal void AssertProfile(string name, bool enabled, params (string Name, string Value)[] variables)
    {
        Wait(
            () => ReadProfiles().EnumerateArray().Any(profile =>
                profile.GetProperty("Name").GetString() == name &&
                profile.GetProperty("IsEnabled").GetBoolean() == enabled &&
                variables.All(variable => profile.GetProperty("Variables").EnumerateArray().Any(entry =>
                    entry.GetProperty("Name").GetString() == variable.Name &&
                    entry.GetProperty("Values").GetString() == variable.Value))),
            $"Profile {name}, enabled={enabled}, was not persisted with the expected variables.");
    }

    internal static JsonElement ReadProfiles()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(TestState.ProfilesPath));
        return document.RootElement.Clone();
    }

    internal static void AssertUserVariable(string name, string? value) =>
        Wait(() => TestState.ReadUserVariable(name) == value, $"User registry value {name} did not become '{value ?? "<absent>"}'.");

    internal void AssertAppliedVariable(string name, string? value)
    {
        Step($"Checking Applied variables: {name}={value ?? "<absent>"}");
        Wait(
            () =>
            {
                var panel = Session.Find<Element>(By.AccessibilityId("AppliedVariablesScrollViewer"));
                string[] text = Nodes(Inspect(panel)).Where(node => Property(node, "type") == "Text")
                    .Select(node => Property(node, "name")).ToArray();
                if (value is null)
                {
                    return !text.Contains(name, StringComparer.OrdinalIgnoreCase);
                }

                return Enumerable.Range(0, Math.Max(0, text.Length - 1)).Any(index =>
                    text[index].Equals(name, StringComparison.OrdinalIgnoreCase) && text[index + 1] == value);
            },
            $"Applied variables did not show {name}={value ?? "<absent>"}.");
    }

    internal static void Wait(Func<bool> condition, string failure)
    {
        var result = WaitHelper.WaitForStable(
            condition,
            match => match,
            timeoutMS: 20_000,
            requiredConsecutiveMatches: 2,
            pollIntervalMS: 200,
            shouldRetryException: exception => exception is IOException or JsonException);
        Assert.IsTrue(result.Succeeded, $"{failure} Last transient error: {result.LastException}");
    }
}
