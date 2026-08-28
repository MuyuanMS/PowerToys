// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public partial class DetailsViewModelTests
{
    private sealed class TestPageContext : IPageContext
    {
        public TaskScheduler Scheduler { get; init; } = new InlineTaskScheduler();

        public ICommandProviderContext ProviderContext => CommandProviderContext.Empty;

        public void ShowException(Exception ex, string? extensionHint = null)
        {
            throw new AssertFailedException($"Unexpected exception from view model: {ex}");
        }
    }

    private sealed class InlineTaskScheduler : TaskScheduler
    {
        protected override IEnumerable<Task>? GetScheduledTasks() => null;

        protected override void QueueTask(Task task) => TryExecuteTask(task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);
    }

    private sealed class QueuedTaskScheduler : TaskScheduler
    {
        private readonly ConcurrentQueue<Task> _tasks = [];

        protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();

        protected override void QueueTask(Task task) => _tasks.Enqueue(task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

        public void ExecuteAllAvailable()
        {
            while (_tasks.TryDequeue(out var task))
            {
                TryExecuteTask(task);
            }
        }

        public void ExecuteUntil(Func<bool> condition)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition())
            {
                ExecuteAllAvailable();
                if (timeout.Elapsed > TimeSpan.FromSeconds(2))
                {
                    Assert.Fail("Timed out waiting for scheduled details work.");
                }

                Thread.Sleep(5);
            }
        }
    }

    private static WeakReference<IPageContext> CreatePageContext()
    {
        var ctx = new TestPageContext();
        return new WeakReference<IPageContext>(ctx);
    }

    private static WeakReference<IPageContext> CreatePageContext(TaskScheduler scheduler)
    {
        var ctx = new TestPageContext { Scheduler = scheduler };
        return new WeakReference<IPageContext>(ctx);
    }

    [TestMethod]
    public void InitializeProperties_SetsBodyAndTitle()
    {
        var details = new Details { Title = "Hello", Body = "World" };
        var vm = new DetailsViewModel(details, CreatePageContext());

        vm.InitializeProperties();

        Assert.AreEqual("Hello", vm.Title);
        Assert.AreEqual("World", vm.Body);
    }

    [TestMethod]
    public void PropChanged_Body_UpdatesViewModelProperty()
    {
        var details = new Details { Title = "Initial", Body = "Initial body" };
        var vm = new DetailsViewModel(details, CreatePageContext());
        vm.InitializeProperties();

        // Act — toolkit Details raises PropChanged synchronously on set
        details.Body = "Updated body";

        // The property value is set synchronously in FetchProperty;
        // ApplyPendingUpdates flushes the PropertyChanged notification queue.
        vm.ApplyPendingUpdates();

        Assert.AreEqual("Updated body", vm.Body);
    }

    [TestMethod]
    public void PropChanged_Title_UpdatesViewModelProperty()
    {
        var details = new Details { Title = "Original", Body = "Text" };
        var vm = new DetailsViewModel(details, CreatePageContext());
        vm.InitializeProperties();

        details.Title = "New Title";
        vm.ApplyPendingUpdates();

        Assert.AreEqual("New Title", vm.Title);
    }

    [TestMethod]
    public void PropChanged_Metadata_RebuildsList()
    {
        var details = new Details
        {
            Title = "T",
            Body = "B",
            Metadata = [],
        };
        var vm = new DetailsViewModel(details, CreatePageContext());
        vm.InitializeProperties();
        Assert.AreEqual(0, vm.Metadata.Count);

        // Act — update metadata with a link element
        details.Metadata = [new DetailsElement { Key = "link", Data = new DetailsLink("http://example.com", "Example") }];
        vm.ApplyPendingUpdates();

        Assert.AreEqual(1, vm.Metadata.Count);
    }

    [TestMethod]
    public void InitializeProperties_CreatesContentViewModels()
    {
        var details = new Details
        {
            Content =
            [
                new MarkdownContent { Body = "Markdown details" },
                new PlainTextContent { Text = "Plain text details" },
                new TreeContent { RootContent = new MarkdownContent { Body = "Root" } },
            ],
        };
        var vm = new DetailsViewModel(details, CreatePageContext());

        vm.InitializeProperties();

        Assert.IsTrue(vm.HasContent);
        Assert.AreEqual(3, vm.Content.Count);
        Assert.IsInstanceOfType<ContentMarkdownViewModel>(vm.Content[0]);
        Assert.IsInstanceOfType<ContentPlainTextViewModel>(vm.Content[1]);
        Assert.IsInstanceOfType<ContentTreeViewModel>(vm.Content[2]);
    }

    [TestMethod]
    public void ItemsChanged_RebuildsContentOnPageScheduler()
    {
        var scheduler = new QueuedTaskScheduler();
        var first = new MarkdownContent { Body = "First" };
        var second = new PlainTextContent { Text = "Second" };
        var details = new Details { Content = [first] };
        var vm = new DetailsViewModel(details, CreatePageContext(scheduler));

        vm.InitializeProperties();
        scheduler.ExecuteUntil(() => vm.Content.Count == 1);

        details.Content = [second];

        Assert.AreEqual(1, vm.Content.Count);
        Assert.IsInstanceOfType<ContentMarkdownViewModel>(vm.Content[0]);

        scheduler.ExecuteUntil(() => vm.Content.Count == 1 && vm.Content[0] is ContentPlainTextViewModel);

        Assert.IsTrue(vm.HasContent);
    }

    [TestMethod]
    public void ContentReplacement_CleansRemovedViewModels()
    {
        var first = new MarkdownContent { Body = "First" };
        var second = new MarkdownContent { Body = "Second" };
        var details = new Details { Content = [first] };
        var vm = new DetailsViewModel(details, CreatePageContext());
        vm.InitializeProperties();

        Assert.AreEqual(1, GetPropChangedSubscriberCount(first));

        details.Content = [second];

        Assert.AreEqual(0, GetPropChangedSubscriberCount(first));
        Assert.AreEqual(1, GetPropChangedSubscriberCount(second));
    }

    [TestMethod]
    public void Cleanup_CleansContentViewModels()
    {
        var content = new MarkdownContent { Body = "Content" };
        var details = new Details { Content = [content] };
        var vm = new DetailsViewModel(details, CreatePageContext());
        vm.InitializeProperties();

        Assert.AreEqual(1, GetPropChangedSubscriberCount(content));

        vm.SafeCleanup();

        Assert.AreEqual(0, vm.Content.Count);
        Assert.IsFalse(vm.HasContent);
        Assert.AreEqual(0, GetPropChangedSubscriberCount(content));
    }

    [TestMethod]
    public void Cleanup_UnsubscribesFromPropChanged()
    {
        var details = new Details { Title = "T", Body = "Original" };
        var vm = new DetailsViewModel(details, CreatePageContext());
        vm.InitializeProperties();

        // Act — cleanup unsubscribes, then change should not propagate
        vm.SafeCleanup();
        details.Body = "After cleanup";

        Assert.AreEqual("Original", vm.Body);
    }

    [TestMethod]
    public void NonObservableDetails_DoesNotThrow()
    {
        // IDetails that does NOT implement INotifyPropChanged
        var details = new NonObservableDetails();
        var vm = new DetailsViewModel(details, CreatePageContext());

        // Should not throw — just doesn't subscribe to anything
        vm.InitializeProperties();

        Assert.AreEqual("Static Title", vm.Title);
        Assert.AreEqual("Static Body", vm.Body);
    }

    /// <summary>
    /// A minimal IDetails that does NOT implement INotifyPropChanged.
    /// </summary>
    private sealed partial class NonObservableDetails : IDetails
    {
        public IIconInfo HeroImage => new IconInfo(string.Empty);

        public string Title => "Static Title";

        public string Body => "Static Body";

        public IDetailsElement[] Metadata => [];
    }

    private static int GetPropChangedSubscriberCount(BaseObservable observable) =>
        ((Delegate?)typeof(BaseObservable)
            .GetField(nameof(BaseObservable.PropChanged), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(observable))?
            .GetInvocationList()
            .Length ?? 0;
}
