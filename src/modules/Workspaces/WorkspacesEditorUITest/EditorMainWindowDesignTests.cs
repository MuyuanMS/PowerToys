// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.PowerToys.UITest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkspacesEditorUITest;

/// <summary>
/// Design validation tests for the Workspaces Editor main window.
/// These verify that all expected UI elements are present and accessible,
/// serving as a contract that the WinUI migration must satisfy.
///
/// Window: MainWindow / WorkspacesEditorPage
/// Tests cover: header elements, action buttons, workspace list, search, sort.
/// </summary>
[TestClass]
public class EditorMainWindowDesignTests : WorkspacesUiAutomationBase
{
    public EditorMainWindowDesignTests()
        : base()
    {
    }

    [TestMethod("MainWindow.Header.TitleTextPresent")]
    [TestCategory("Design.MainWindow")]
    public void MainWindow_HasWorkspacesTitleText()
    {
        Assert.IsTrue(Has<TextBlock>(By.Name("Workspaces")), "Should display 'Workspaces' title");
    }

    [TestMethod("MainWindow.Header.CreateWorkspaceButtonPresent")]
    [TestCategory("Design.MainWindow")]
    public void MainWindow_HasCreateWorkspaceButton()
    {
        Assert.IsTrue(Has<Button>("Create Workspace"), "Should have 'Create Workspace' button");
    }

    [TestMethod("MainWindow.Header.SearchBoxPresent")]
    [TestCategory("Design.MainWindow")]
    public void MainWindow_HasSearchBox()
    {
        Assert.IsTrue(
            Has<TextBox>(By.AccessibilityId("SearchBox")) || Has<TextBox>(By.Name("Search")),
            "Should have a search input");
    }

    [TestMethod("MainWindow.Header.SortByPresent")]
    [TestCategory("Design.MainWindow")]
    public void MainWindow_HasSortByDropdown()
    {
        Assert.IsTrue(
            Has<Button>("Sort by"),
            "Should have a 'Sort by' menu button");
    }

    [TestMethod("MainWindow.Content.WorkspacesListPresent")]
    [TestCategory("Design.MainWindow")]
    public void MainWindow_HasWorkspacesList()
    {
        // The workspaces list container should exist even when empty
        Assert.IsTrue(
            Has<Element>(By.AccessibilityId("WorkspacesItemsControl")),
            "Should have workspace list container");
    }

    [TestMethod("MainWindow.Content.EmptyStateMessagePresent")]
    [TestCategory("Design.MainWindow")]
    public void MainWindow_EmptyState_ShowsMessage()
    {
        // When no workspaces exist, should show a message
        var hasEmptyMessage = Has<TextBlock>(By.Name("There are no saved Workspaces"))
            || Has<TextBlock>(By.Name("No saved Workspaces"));

        var workspacesList = Find<Element>(By.AccessibilityId("WorkspacesItemsControl"));
        if (workspacesList.FindAll<Element>(By.AccessibilityId("WorkspaceItem")).Count == 0)
        {
            Assert.IsTrue(hasEmptyMessage, "Empty state should show a message when no workspaces exist");
        }
    }

    [TestMethod("MainWindow.Keyboard.TabNavigationWorks")]
    [TestCategory("Design.MainWindow")]
    public void MainWindow_TabNavigation_MovesForwardThroughControls()
    {
        // Press Tab and verify focus moves to an interactive element
        SendKeys(Key.Tab);
        Task.Delay(500).Wait();

        var focusedElements = FindAll<Element>(By.XPath("//*[@HasKeyboardFocus='true']"));
        Assert.IsTrue(
            focusedElements.Any(element => element.ControlType is "ControlType.Button" or "ControlType.Edit" or "ControlType.ComboBox" or "ControlType.ListItem"),
            "Tab should move focus to an interactive control.");
    }

    [TestMethod("MainWindow.Accessibility.CreateButtonHasAutomationName")]
    [TestCategory("Design.MainWindow")]
    public void MainWindow_CreateButton_HasAccessibleName()
    {
        var button = Find<Button>("Create Workspace");
        Assert.IsNotNull(button, "Create Workspace button should be findable by its accessible name");
    }
}
