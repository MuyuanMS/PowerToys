// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.PowerToys.UITest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkspacesEditorUITest;

/// <summary>
/// Design validation tests for the Workspace Editing page.
/// This page appears when a user clicks "Edit" on a workspace
/// and shows the app list with positioning controls.
///
/// UI elements that must be preserved:
/// - Workspace name text box
/// - App list with per-app controls
/// - Save button and title-bar Back button
/// - Position controls (X, Y, Width, Height or Maximized/Minimized dropdown)
/// </summary>
[TestClass]
public class EditingPageDesignTests : WorkspacesUiAutomationBase
{
    public EditingPageDesignTests()
        : base()
    {
    }

    [TestInitialize]
    public void Setup()
    {
        // Ensure at least one workspace exists
        AttachWorkspacesEditor();
        var workspacesList = Find<Element>(By.AccessibilityId("WorkspacesItemsControl"));
        if (workspacesList.FindAll<Element>(By.ClassName("WorkspaceItem")).Count == 0)
        {
            CreateTestWorkspace("EditDesignTest");
            Task.Delay(2000).Wait();
        }
    }

    [TestMethod("EditingPage.HasNameTextBox")]
    [TestCategory("Design.EditingPage")]
    public void EditingPage_HasWorkspaceNameInput()
    {
        NavigateToEditPage();

        Assert.IsTrue(
            Has<TextBox>(By.AccessibilityId("EditNameTextBox")) || Has<TextBox>(By.Name("Workspace name")),
            "Editing page should have a workspace name text box");

        CancelAndReturn();
    }

    [TestMethod("EditingPage.HasSaveButton")]
    [TestCategory("Design.EditingPage")]
    public void EditingPage_HasSaveButton()
    {
        NavigateToEditPage();

        Assert.IsTrue(
            Has<Button>("Save Workspace") || Has<Button>("Save"),
            "Editing page should have a Save button");

        CancelAndReturn();
    }

    [TestMethod("EditingPage.HasBackButton")]
    [TestCategory("Design.EditingPage")]
    public void EditingPage_HasBackButton()
    {
        NavigateToEditPage();

        Assert.IsTrue(Has<Button>("Back"), "Editing page should have a title-bar Back button");

        CancelAndReturn();
    }

    [TestMethod("EditingPage.HasLaunchAndEditButton")]
    [TestCategory("Design.EditingPage")]
    public void EditingPage_HasLaunchAndEditButton()
    {
        NavigateToEditPage();

        Assert.IsTrue(
            Has<Button>("Launch & Edit") || Has<Button>("Launch and Edit"),
            "Editing page should have a 'Launch & Edit' button");

        CancelAndReturn();
    }

    [TestMethod("EditingPage.HasAppList")]
    [TestCategory("Design.EditingPage")]
    public void EditingPage_HasApplicationsList()
    {
        NavigateToEditPage();

        // Should have some app items visible
        Assert.IsTrue(
            Has<Element>(By.AccessibilityId("CapturedAppList")),
            "Editing page should have an application list");

        CancelAndReturn();
    }

    [TestMethod("EditingPage.Back_ReturnsToMainPage")]
    [TestCategory("Design.EditingPage")]
    public void EditingPage_Back_ReturnsToMainList()
    {
        NavigateToEditPage();

        Find<Button>("Back").Click();
        Task.Delay(1000).Wait();

        Assert.IsTrue(Has<Button>("Create Workspace"), "After cancel, should return to main page");
    }

    private void NavigateToEditPage()
    {
        AttachWorkspacesEditor();
        var root = Find<Element>(By.AccessibilityId("WorkspacesItemsControl"));
        var items = root.FindAll<Element>(By.ClassName("WorkspaceItem"));
        Assert.IsTrue(items.Count > 0, "Expected a workspace item to edit.");
        items[0].Click();
        Task.Delay(1000).Wait();
    }

    private void CancelAndReturn()
    {
        try
        {
            Find<Button>("Back").Click();
            Task.Delay(500).Wait();
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
