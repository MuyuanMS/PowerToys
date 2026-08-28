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

        public bool ThrowOnException { get; init; } = true;

        public Exception? LastException { get; private set; }

        public void ShowException(Exception ex, string? extensionHint = null)
        {
            LastException = ex;
            if (!ThrowOnException)
            {
                return;
            }

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

    private sealed record PageContextFixture(TestPageContext Context, WeakReference<IPageContext> Reference);

    private static PageContextFixture CreatePageContext()
    {
        var ctx = new TestPageContext();
        return new(ctx, new WeakReference<IPageContext>(ctx));
    }

    private static PageContextFixture CreatePageContext(TaskScheduler scheduler)
    {
        var ctx = new TestPageContext { Scheduler = scheduler };
        return new(ctx, new WeakReference<IPageContext>(ctx));
    }

    private static PageContextFixture CreatePageContext(bool throwOnException)
    {
        var ctx = new TestPageContext { ThrowOnException = throwOnException };
        return new(ctx, new WeakReference<IPageContext>(ctx));
    }

    [TestMethod]
    public void InitializeProperties_SetsBodyAndTitle()
    {
        var details = new Details { Title = "Hello", Body = "World" };
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);

        vm.InitializeProperties();

        Assert.AreEqual("Hello", vm.Title);
        Assert.AreEqual("World", vm.Body);
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void PropChanged_Body_UpdatesViewModelProperty()
    {
        var details = new Details { Title = "Initial", Body = "Initial body" };
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);
        vm.InitializeProperties();

        // Act — toolkit Details raises PropChanged synchronously on set
        details.Body = "Updated body";

        // The property value is set synchronously in FetchProperty;
        // ApplyPendingUpdates flushes the PropertyChanged notification queue.
        vm.ApplyPendingUpdates();

        Assert.AreEqual("Updated body", vm.Body);
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void PropChanged_Title_UpdatesViewModelProperty()
    {
        var details = new Details { Title = "Original", Body = "Text" };
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);
        vm.InitializeProperties();

        details.Title = "New Title";
        vm.ApplyPendingUpdates();

        Assert.AreEqual("New Title", vm.Title);
        GC.KeepAlive(context.Context);
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
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);
        vm.InitializeProperties();
        Assert.AreEqual(0, vm.Metadata.Count);

        // Act — update metadata with a link element
        details.Metadata = [new DetailsElement { Key = "link", Data = new DetailsLink("http://example.com", "Example") }];
        vm.ApplyPendingUpdates();

        Assert.AreEqual(1, vm.Metadata.Count);
        GC.KeepAlive(context.Context);
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
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);

        vm.InitializeProperties();

        Assert.IsTrue(vm.HasContent);
        Assert.AreEqual(3, vm.Content.Count);
        Assert.IsInstanceOfType<ContentMarkdownViewModel>(vm.Content[0]);
        Assert.IsInstanceOfType<ContentPlainTextViewModel>(vm.Content[1]);
        Assert.IsInstanceOfType<ContentTreeViewModel>(vm.Content[2]);
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void ItemsChanged_RebuildsContentOnPageScheduler()
    {
        var scheduler = new QueuedTaskScheduler();
        var first = new MarkdownContent { Body = "First" };
        var second = new PlainTextContent { Text = "Second" };
        var details = new Details { Content = [first] };
        var context = CreatePageContext(scheduler);
        var vm = new DetailsViewModel(details, context.Reference);

        vm.InitializeProperties();
        scheduler.ExecuteUntil(() => vm.Content.Count == 1);

        details.Content = [second];

        Assert.AreEqual(1, vm.Content.Count);
        Assert.IsInstanceOfType<ContentMarkdownViewModel>(vm.Content[0]);

        scheduler.ExecuteUntil(() => vm.Content.Count == 1 && vm.Content[0] is ContentPlainTextViewModel);

        Assert.IsTrue(vm.HasContent);
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void ConcurrentItemsChanged_IgnoresStaleContentRebuild()
    {
        using var slowGetContentStarted = new ManualResetEventSlim();
        using var releaseSlowGetContent = new ManualResetEventSlim();
        var scheduler = new QueuedTaskScheduler();
        var first = new MarkdownContent { Body = "First" };
        var second = new PlainTextContent { Text = "Second" };
        var third = new PlainTextContent { Text = "Third" };
        var details = new TestDetailsWithQueuedContent { Content = [first] };
        var context = CreatePageContext(scheduler);
        var vm = new DetailsViewModel(details, context.Reference);

        vm.InitializeProperties();
        scheduler.ExecuteUntil(() => vm.Content.Count == 1);

        details.EnqueueContent(() =>
        {
            slowGetContentStarted.Set();
            releaseSlowGetContent.Wait(TimeSpan.FromSeconds(2));
            return [second];
        });
        var slowRefresh = Task.Run(details.TriggerItemsChanged);

        Assert.IsTrue(slowGetContentStarted.Wait(TimeSpan.FromSeconds(2)));

        details.EnqueueContent(() => [third]);
        details.TriggerItemsChanged();
        scheduler.ExecuteUntil(() => vm.Content.Count == 1 && vm.Content[0] is ContentPlainTextViewModel plainText && plainText.Text == "Third");

        releaseSlowGetContent.Set();
        Assert.IsTrue(slowRefresh.Wait(TimeSpan.FromSeconds(2)));
        scheduler.ExecuteAllAvailable();

        Assert.AreEqual(1, vm.Content.Count);
        Assert.IsInstanceOfType<ContentPlainTextViewModel>(vm.Content[0]);
        var current = (ContentPlainTextViewModel)vm.Content[0];
        Assert.AreEqual("Third", current.Text);
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void ContentReplacement_CleansRemovedViewModels()
    {
        var first = new MarkdownContent { Body = "First" };
        var second = new MarkdownContent { Body = "Second" };
        var details = new Details { Content = [first] };
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);
        vm.InitializeProperties();

        Assert.AreEqual(1, GetPropChangedSubscriberCount(first));

        details.Content = [second];

        Assert.AreEqual(0, GetPropChangedSubscriberCount(first));
        Assert.AreEqual(1, GetPropChangedSubscriberCount(second));
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void FormContentReplacement_CleansRemovedViewModel()
    {
        var form = new FormContent { TemplateJson = ValidAdaptiveCardJson };
        var replacement = new MarkdownContent { Body = "Replacement" };
        var details = new Details { Content = [form] };
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);
        vm.InitializeProperties();

        Assert.AreEqual(1, GetPropChangedSubscriberCount(form));

        details.Content = [replacement];

        Assert.AreEqual(0, GetPropChangedSubscriberCount(form));
        Assert.AreEqual(1, GetPropChangedSubscriberCount(replacement));
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void ContentInitializationException_CleansInitializedViewModels()
    {
        var content = new MarkdownContent { Body = "Content" };
        var throwingContent = new ThrowingMarkdownContent();
        var details = new Details { Content = [content, throwingContent] };
        var context = CreatePageContext(throwOnException: false);
        var vm = new DetailsViewModel(details, context.Reference);

        vm.InitializeProperties();

        Assert.IsInstanceOfType<InvalidOperationException>(context.Context.LastException);
        Assert.AreEqual(0, vm.Content.Count);
        Assert.AreEqual(0, GetPropChangedSubscriberCount(content));
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void Cleanup_CleansContentViewModels()
    {
        var content = new MarkdownContent { Body = "Content" };
        var details = new Details { Content = [content] };
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);
        vm.InitializeProperties();

        Assert.AreEqual(1, GetPropChangedSubscriberCount(content));

        vm.SafeCleanup();

        Assert.AreEqual(0, vm.Content.Count);
        Assert.IsFalse(vm.HasContent);
        Assert.AreEqual(0, GetPropChangedSubscriberCount(content));
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void Cleanup_ClearsContentOnPageScheduler()
    {
        var scheduler = new QueuedTaskScheduler();
        var content = new MarkdownContent { Body = "Content" };
        var details = new Details { Content = [content] };
        var context = CreatePageContext(scheduler);
        var vm = new DetailsViewModel(details, context.Reference);
        vm.InitializeProperties();
        scheduler.ExecuteUntil(() => vm.Content.Count == 1);

        vm.SafeCleanup();

        scheduler.ExecuteUntil(() => vm.Content.Count == 0);
        Assert.IsFalse(vm.HasContent);
        Assert.AreEqual(0, GetPropChangedSubscriberCount(content));
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void Cleanup_UnsubscribesFromPropChanged()
    {
        var details = new Details { Title = "T", Body = "Original" };
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);
        vm.InitializeProperties();

        // Act — cleanup unsubscribes, then change should not propagate
        vm.SafeCleanup();
        details.Body = "After cleanup";

        Assert.AreEqual("Original", vm.Body);
        GC.KeepAlive(context.Context);
    }

    [TestMethod]
    public void NonObservableDetails_DoesNotThrow()
    {
        // IDetails that does NOT implement INotifyPropChanged
        var details = new NonObservableDetails();
        var context = CreatePageContext();
        var vm = new DetailsViewModel(details, context.Reference);

        // Should not throw — just doesn't subscribe to anything
        vm.InitializeProperties();

        Assert.AreEqual("Static Title", vm.Title);
        Assert.AreEqual("Static Body", vm.Body);
        GC.KeepAlive(context.Context);
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

    private sealed partial class TestDetailsWithQueuedContent : Details
    {
        private readonly ConcurrentQueue<Func<IContent[]>> _queuedContent = [];

        public void EnqueueContent(Func<IContent[]> content) => _queuedContent.Enqueue(content);

        public void TriggerItemsChanged() => RaiseItemsChanged();

        public override IContent[] GetContent() => _queuedContent.TryDequeue(out var content)
            ? content()
            : base.GetContent();
    }

    private sealed partial class ThrowingMarkdownContent : MarkdownContent
    {
        public override string Body
        {
            get => throw new InvalidOperationException("Test content failed.");
            set => base.Body = value;
        }
    }

    private const string ValidAdaptiveCardJson = """
{
    "$schema": "https://adaptivecards.io/schemas/adaptive-card.json",
    "type": "AdaptiveCard",
    "version": "1.5",
    "body": []
}
""";

    private static int GetPropChangedSubscriberCount(BaseObservable observable) =>
        ((Delegate?)typeof(BaseObservable)
            .GetField(nameof(BaseObservable.PropChanged), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(observable))?
            .GetInvocationList()
            .Length ?? 0;
}
