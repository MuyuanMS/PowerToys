// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CmdPal.UI.ViewModels.Messages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public partial class TabbedPageViewModelTests
{
    private sealed partial class TestAppExtensionHost : AppExtensionHost
    {
        public override string? GetExtensionDisplayName() => "Test Host";
    }

    private sealed partial class TestTabbedPage : TabbedPage
    {
        private ITab[] _tabs;

        public TestTabbedPage(ITab[] tabs)
        {
            _tabs = tabs;
        }

        public override ITab[] GetTabs() => _tabs;

        public void SetTabs(ITab[] tabs)
        {
            _tabs = tabs;
            RaiseItemsChanged(tabs.Length);
        }
    }

    private sealed partial class TestListPage : ListPage
    {
        public TestListPage(string id)
        {
            Id = id;
            Name = id;
            Title = id;
        }

        public override IListItem[] GetItems() => [new ListItem(new NoOpCommand() { Name = "Item" })];
    }

    private sealed partial class TestContentPage : ContentPage
    {
        public TestContentPage(string id)
        {
            Id = id;
            Name = id;
            Title = id;
        }

        public override IContent[] GetContent() => [];
    }

    private sealed partial class TestContentPageWithoutId : ContentPage
    {
        public TestContentPageWithoutId(string title)
        {
            Title = title;
            Name = title;
        }

        public override IContent[] GetContent() => [];
    }

    private sealed partial class TestTabbedPageAsChild : Microsoft.CommandPalette.Extensions.Toolkit.TabbedPage
    {
        public TestTabbedPageAsChild(string id)
        {
            Id = id;
            Name = id;
            Title = id;
        }

        public override ITab[] GetTabs() => [];
    }

    private sealed partial class TestParametersPage : Page, IParametersPage
    {
        public TestParametersPage(string id)
        {
            Id = id;
            Name = id;
            Title = id;
            Command = new ListItem(new NoOpCommand() { Name = $"Run {id}" });
        }

        public IParameterRun[] Parameters => [];

        public IListItem Command { get; }
    }

    private static CommandPalettePageViewModelFactory CreateFactory() =>
        new(TaskScheduler.Default, DefaultContextMenuFactory.Instance);

    private static TabbedPageViewModel CreateViewModel(TestTabbedPage page) =>
        new(page, TaskScheduler.Default, new TestAppExtensionHost(), CommandProviderContext.Empty, CreateFactory());

    private static async Task WaitFor(Func<bool> condition, string message, int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(15);
        }

        Assert.IsTrue(condition(), message);
    }

    [TestMethod]
    public async Task InitializeProperties_BuildsTabsAndSelectsFirst()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Issues", new TestListPage("issues")),
            new Tab("Docs", new TestContentPage("docs")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.Tabs.Count == 2 && viewModel.SelectedTab is not null, "Tabs did not populate");

        Assert.IsTrue(viewModel.HasTabs);
        Assert.AreEqual(2, viewModel.Tabs.Count);
        Assert.AreSame(viewModel.Tabs[0], viewModel.SelectedTab);
        Assert.AreEqual("Issues", viewModel.Tabs[0].Title);
        Assert.AreEqual("Docs", viewModel.Tabs[1].Title);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task HasSearchBox_TrueWhenActiveTabIsSearchable()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Issues", new TestListPage("issues")),
            new Tab("Docs", new TestContentPage("docs")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        // The first tab is the default active tab and it is a list page, so the
        // shared search box is active.
        await WaitFor(() => viewModel.SelectedTab is not null, "Tab was not selected");
        await WaitFor(() => viewModel.HasSearchBox, "Search box was not activated for the list tab");

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task HasSearchBox_FalseWhenActiveTabIsNotSearchable()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Docs", new TestContentPage("docs")),
            new Tab("Issues", new TestListPage("issues")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        // The first (active) tab is a content page, so the shared search box is
        // deactivated even though a later tab is searchable.
        await WaitFor(() => viewModel.SelectedTab is not null, "Tab was not selected");
        await WaitFor(() => !viewModel.HasSearchBox, "Search box was not deactivated for the content tab");

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task HasSearchBox_FollowsActiveTabOnSwitch()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Docs", new TestContentPage("docs")),
            new Tab("Issues", new TestListPage("issues")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.Tabs.Count == 2, "Tabs did not populate");

        // Content tab active: search box deactivated.
        await WaitFor(() => !viewModel.HasSearchBox, "Search box was not deactivated for the content tab");

        // Switch to the list tab: search box activates.
        viewModel.SelectedTab = viewModel.Tabs[1];
        await WaitFor(() => viewModel.HasSearchBox, "Search box was not activated after switching to the list tab");

        // Switch back to the content tab: search box deactivates again.
        viewModel.SelectedTab = viewModel.Tabs[0];
        await WaitFor(() => !viewModel.HasSearchBox, "Search box was not deactivated after switching back to the content tab");

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task FirstTab_LazilyCreatesActiveChild()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Issues", new TestListPage("issues")),
            new Tab("Docs", new TestContentPage("docs")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.ActiveChild is not null, "Active child was not created");

        Assert.IsInstanceOfType(viewModel.ActiveChild, typeof(ListViewModel));
        Assert.IsFalse(viewModel.ShowUnsupportedPlaceholder);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task UnsupportedTab_YieldsNoActiveChildAndShowsPlaceholder()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Bare", new Page() { Id = "bare", Name = "Bare", Title = "Bare" }),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.SelectedTab is not null, "Tab was not selected");

        // Give ActivateTab a chance to resolve the (null) child.
        await WaitFor(() => viewModel.ShowUnsupportedPlaceholder, "Placeholder was not shown for unsupported tab");
        Assert.IsNull(viewModel.ActiveChild);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task Badge_PropChangedPropagatesToTabViewModel()
    {
        var tab = new Tab("Issues", new TestContentPage("issues"));
        var page = new TestTabbedPage([tab]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.Tabs.Count == 1, "Tabs did not populate");
        Assert.IsFalse(viewModel.Tabs[0].HasBadge);

        tab.Badge = "10";

        await WaitFor(() => viewModel.Tabs[0].Badge == "10", "Badge did not propagate");
        Assert.IsTrue(viewModel.Tabs[0].HasBadge);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task SearchText_ForwardsToActiveListTab()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Issues", new TestListPage("issues")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.ActiveChild is ListViewModel, "List child was not created");

        var listChild = (ListViewModel)viewModel.ActiveChild!;
        viewModel.SearchTextBox = "bug";

        await WaitFor(() => listChild.SearchTextBox == "bug", "Search text was not forwarded");

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task ItemsChanged_PreservesActiveTabById()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Issues", new TestListPage("issues")),
            new Tab("Docs", new TestContentPage("docs")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.Tabs.Count == 2 && viewModel.SelectedTab is not null, "Tabs did not populate");

        viewModel.SelectedTab = viewModel.Tabs[1];
        Assert.AreEqual("page:docs", viewModel.SelectedTab!.TabId);

        // Dynamic update: the extension re-publishes the tabs (new instances) and
        // adds a third. The active tab identity ("docs") must be preserved.
        page.SetTabs(
        [
            new Tab("Issues", new TestListPage("issues")),
            new Tab("Docs", new TestContentPage("docs")),
            new Tab("Actions", new TestContentPage("actions")),
        ]);

        await WaitFor(() => viewModel.Tabs.Count == 3, "Tabs did not update");
        await WaitFor(() => viewModel.SelectedTab?.TabId == "page:docs", "Active tab was not preserved");

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task DuplicateTitlesWithoutPageIds_GetDistinctFallbackIdentities()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Duplicate", new TestContentPageWithoutId("First")),
            new Tab("Duplicate", new TestContentPageWithoutId("Second")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.Tabs.Count == 2, "Tabs did not populate");

        Assert.AreNotEqual(viewModel.Tabs[0].TabId, viewModel.Tabs[1].TabId);

        viewModel.SelectedTab = viewModel.Tabs[1];
        await WaitFor(() => viewModel.ActiveChild?.Title == "Second", "Second tab child was not activated");

        viewModel.SelectedTab = viewModel.Tabs[0];
        await WaitFor(() => viewModel.ActiveChild?.Title == "First", "First tab child was not activated");

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task ItemsChanged_RecreatesCachedChildWhenPageInstanceChanges()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Docs", new TestContentPage("docs")) { Id = "docs-tab" },
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.ActiveChild is not null, "Initial child was not created");
        var firstChild = viewModel.ActiveChild;

        page.SetTabs(
        [
            new Tab("Docs", new TestContentPage("docs-updated")) { Id = "docs-tab" },
        ]);

        await WaitFor(() => viewModel.ActiveChild is not null && !ReferenceEquals(viewModel.ActiveChild, firstChild), "Cached child was not recreated");
        Assert.AreEqual("docs-updated", viewModel.ActiveChild!.Id);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task TabPagePropertyChange_RecreatesActiveChild()
    {
        var tab = new Tab("Docs", new TestContentPage("docs")) { Id = "docs-tab" };
        var page = new TestTabbedPage([tab]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.ActiveChild is not null, "Initial child was not created");
        var firstChild = viewModel.ActiveChild;

        tab.Page = new TestContentPage("docs-updated");
        viewModel.Tabs[0].ApplyPendingUpdates();

        await WaitFor(() => viewModel.ActiveChild is not null && !ReferenceEquals(viewModel.ActiveChild, firstChild), "Active child was not recreated after Page changed");
        Assert.AreEqual("docs-updated", viewModel.ActiveChild!.Id);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task TabIdAndPagePropertyChange_RecreatesActiveChild()
    {
        var tab = new Tab("Docs", new TestContentPage("docs")) { Id = "docs-tab" };
        var page = new TestTabbedPage([tab]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.ActiveChild is not null, "Initial child was not created");
        var firstChild = viewModel.ActiveChild;

        tab.Id = "docs-tab-updated";
        tab.Page = new TestContentPage("docs-updated");
        viewModel.Tabs[0].ApplyPendingUpdates();

        await WaitFor(() => viewModel.ActiveChild is not null && !ReferenceEquals(viewModel.ActiveChild, firstChild), "Active child was not recreated after Id and Page changed");
        Assert.AreEqual("tab:docs-tab-updated", viewModel.SelectedTab!.TabId);
        Assert.AreEqual("docs-updated", viewModel.ActiveChild!.Id);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task ActivateCachedTab_DoesNotOverwriteChildSearchText()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Issues", new TestListPage("issues")),
            new Tab("Docs", new TestContentPage("docs")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.ActiveChild is ListViewModel, "List child was not created");
        var listChild = (ListViewModel)viewModel.ActiveChild!;
        listChild.SearchTextBox = "preserved query";

        viewModel.SelectedTab = viewModel.Tabs[1];
        await WaitFor(() => viewModel.ActiveChild is ContentPageViewModel, "Content child was not activated");

        viewModel.SelectedTab = viewModel.Tabs[0];
        await WaitFor(() => ReferenceEquals(viewModel.ActiveChild, listChild), "List child was not reactivated");

        Assert.AreEqual("preserved query", listChild.SearchTextBox);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task NestedTabbedPage_ShowsUnsupportedPlaceholder()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Nested", new TestTabbedPageAsChild("nested")),
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.SelectedTab is not null, "Tab was not selected");
        await WaitFor(() => viewModel.ShowUnsupportedPlaceholder, "Placeholder was not shown for nested tabbed page");
        Assert.IsNull(viewModel.ActiveChild);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task TabIds_UseDisjointNamespacesAndDuplicateSuffixes()
    {
        var firstDuplicate = new Tab("One", new TestContentPage("shared-page")) { Id = "shared" };
        var secondDuplicate = new Tab("Two", new TestContentPage("other-page")) { Id = "shared" };
        var page = new TestTabbedPage(
        [
            new Tab("Raw zero", new TestContentPageWithoutId("No page id")) { Id = "0" },
            new Tab("Fallback zero", new TestContentPageWithoutId("Fallback zero")),
            new Tab("Page id", new TestContentPage("0")),
            firstDuplicate,
            secondDuplicate,
        ]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.Tabs.Count == 5, "Tabs did not populate");

        Assert.AreEqual("tab:0", viewModel.Tabs[0].TabId);
        Assert.AreEqual("fallback:1", viewModel.Tabs[1].TabId);
        Assert.AreEqual("page:0", viewModel.Tabs[2].TabId);
        Assert.AreEqual("tab:shared", viewModel.Tabs[3].TabId);
        Assert.AreEqual("tab:shared|duplicate:4", viewModel.Tabs[4].TabId);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task TabIdPropertyChange_PrunesOldCacheAndRecreatesActiveChild()
    {
        var tab = new Tab("Docs", new TestContentPage("docs")) { Id = "docs-tab" };
        var page = new TestTabbedPage([tab]);

        var viewModel = CreateViewModel(page);
        viewModel.InitializeProperties();

        await WaitFor(() => viewModel.ActiveChild is not null, "Initial child was not created");
        var firstChild = viewModel.ActiveChild;

        tab.Id = "docs-tab-updated";
        viewModel.Tabs[0].ApplyPendingUpdates();

        await WaitFor(() => viewModel.ActiveChild is not null && !ReferenceEquals(viewModel.ActiveChild, firstChild), "Active child was not recreated after Id changed");
        Assert.AreEqual("tab:docs-tab-updated", viewModel.SelectedTab!.TabId);

        viewModel.SafeCleanup();
    }

    [TestMethod]
    public async Task InactiveCachedList_DoesNotPublishCommandContext()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Issues", new TestListPage("issues")),
            new Tab("Docs", new TestContentPage("docs")),
        ]);
        var recipient = new object();
        var commandMessages = 0;

        try
        {
            var viewModel = CreateViewModel(page);
            viewModel.InitializeProperties();

            await WaitFor(() => viewModel.ActiveChild is ListViewModel, "List child was not created");
            var listChild = (ListViewModel)viewModel.ActiveChild!;

            WeakReferenceMessenger.Default.Register<UpdateCommandBarMessage>(recipient, (_, _) => commandMessages++);
            await Task.Delay(200);
            commandMessages = 0;

            listChild.CanPublishContextUpdates = false;
            listChild.RefreshCurrentCommandContext();
            await Task.Delay(100);

            Assert.AreEqual(0, commandMessages);

            listChild.CanPublishContextUpdates = true;
            listChild.RefreshCurrentCommandContext();

            await WaitFor(() => commandMessages > 0, "Active list child did not publish command context");

            viewModel.SafeCleanup();
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    [TestMethod]
    public async Task ParametersTab_ClearsPreviousCommandContext()
    {
        var page = new TestTabbedPage(
        [
            new Tab("Issues", new TestListPage("issues")),
            new Tab("Parameters", new TestParametersPage("parameters")),
        ]);
        var recipient = new object();
        ICommandBarContext? lastCommandContext = null;
        var hideDetailsMessages = 0;
        string? lastSuggestion = null;

        WeakReferenceMessenger.Default.Register<UpdateCommandBarMessage>(recipient, (_, message) => lastCommandContext = message.ViewModel);
        WeakReferenceMessenger.Default.Register<HideDetailsMessage>(recipient, (_, _) => hideDetailsMessages++);
        WeakReferenceMessenger.Default.Register<UpdateSuggestionMessage>(recipient, (_, message) => lastSuggestion = message.TextToSuggest);

        try
        {
            var viewModel = CreateViewModel(page);
            viewModel.InitializeProperties();

            await WaitFor(() => viewModel.ActiveChild is ListViewModel, "List child was not created");
            lastCommandContext = new ContentPageViewModel(new TestContentPage("previous"), TaskScheduler.Default, new TestAppExtensionHost(), CommandProviderContext.Empty);

            viewModel.SelectedTab = viewModel.Tabs[1];

            await WaitFor(() => viewModel.ActiveChild is ParametersPageViewModel, "Parameters child was not activated");
            viewModel.RefreshActiveChildContext();
            await WaitFor(() => lastCommandContext is null && hideDetailsMessages > 0 && lastSuggestion == string.Empty, "Parameters tab did not clear prior context");

            viewModel.SafeCleanup();
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }
}
