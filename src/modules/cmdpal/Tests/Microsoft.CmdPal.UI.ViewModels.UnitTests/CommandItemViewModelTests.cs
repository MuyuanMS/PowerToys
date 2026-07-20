// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CmdPal.Common.Text;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public partial class CommandItemViewModelTests
{
    private sealed class TestPageContext : IPageContext
    {
        private readonly TaskScheduler _scheduler;

        public TestPageContext(TaskScheduler? scheduler = null)
        {
            _scheduler = scheduler ?? TaskScheduler.Default;
        }

        public TaskScheduler Scheduler => _scheduler;

        public ICommandProviderContext ProviderContext => CommandProviderContext.Empty;

        public void ShowException(Exception ex, string? extensionHint = null)
        {
            throw new AssertFailedException($"Unexpected exception from view model: {ex}");
        }
    }

    private sealed class GatedSingleThreadTaskScheduler : TaskScheduler, IDisposable
    {
        private readonly BlockingCollection<Task> _tasks = new();
        private readonly ManualResetEventSlim _canRun;
        private readonly Thread _thread;
        private int _queuedCount;

        public GatedSingleThreadTaskScheduler(bool initiallyOpen = false)
        {
            _canRun = new ManualResetEventSlim(initiallyOpen);
            _thread = new Thread(RunOnSchedulerThread)
            {
                IsBackground = true,
                Name = nameof(GatedSingleThreadTaskScheduler),
            };
            _thread.Start();
        }

        public int SchedulerThreadId { get; private set; }

        protected override IEnumerable<Task>? GetScheduledTasks() => _tasks.ToArray();

        protected override void QueueTask(Task task)
        {
            Interlocked.Increment(ref _queuedCount);
            _tasks.Add(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
            => Environment.CurrentManagedThreadId == SchedulerThreadId && _canRun.IsSet && TryExecuteTask(task);

        public void Release() => _canRun.Set();

        public bool WaitForQueuedTaskCount(int expectedCount, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Volatile.Read(ref _queuedCount) >= expectedCount)
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }

        public void Dispose()
        {
            _tasks.CompleteAdding();
            _canRun.Set();
            _thread.Join(TimeSpan.FromSeconds(5));
            _tasks.Dispose();
            _canRun.Dispose();
        }

        private void RunOnSchedulerThread()
        {
            SchedulerThreadId = Environment.CurrentManagedThreadId;
            foreach (var task in _tasks.GetConsumingEnumerable())
            {
                _canRun.Wait();
                TryExecuteTask(task);
            }
        }
    }

    private sealed partial class TrackingCommandItem : ICommandItem
    {
        private TypedEventHandler<object, IPropChangedEventArgs>? _propChanged;

        public string Title { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public ICommand? Command { get; set; } = new NoOpCommand { Name = "Primary" };

        public IIconInfo? Icon { get; set; }

        public IContextItem[] MoreCommands { get; set; } = [];

        public int AddCount { get; private set; }

        public int RemoveCount { get; private set; }

        public int AddThreadId { get; private set; }

        public int RemoveThreadId { get; private set; }

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged
        {
            add
            {
                AddThreadId = Environment.CurrentManagedThreadId;
                AddCount++;
                _propChanged += value;
            }

            remove
            {
                RemoveThreadId = Environment.CurrentManagedThreadId;
                RemoveCount++;
                _propChanged -= value;
            }
        }

        public void RaisePropertyChanged(string propertyName)
            => _propChanged?.Invoke(this, new PropChangedEventArgs(propertyName));
    }

    [TestMethod]
    public void MoreCommandsAndAllCommands_ReturnSnapshots()
    {
        // The public getters should return cached read-only snapshots, so
        // repeated reads don't allocate a new list when the backing data hasn't
        // changed.
        var pageContext = new TestPageContext();
        var item = new CommandItem(new NoOpCommand { Name = "Primary" })
        {
            Title = "Primary",
            MoreCommands =
            [
                new CommandContextItem(new NoOpCommand { Name = "Secondary" }),
            ],
        };

        var viewModel = new CommandItemViewModel(new(item), new(pageContext), DefaultContextMenuFactory.Instance);
        viewModel.SlowInitializeProperties();

        var moreCommands = viewModel.MoreCommands;
        var allCommands = viewModel.AllCommands;

        Assert.AreSame(moreCommands, viewModel.MoreCommands);
        Assert.AreSame(allCommands, viewModel.AllCommands);
        Assert.AreEqual(1, moreCommands.Count);
        Assert.AreEqual(2, allCommands.Count);
    }

    [TestMethod]
    public void SecondaryCommand_IgnoresLeadingSeparators()
    {
        // SecondaryCommand/HasMoreCommands should be derived from the first actual command item,
        // not from the raw first entry in MoreCommands.
        var pageContext = new TestPageContext();
        var item = new CommandItem(new NoOpCommand { Name = "Primary" })
        {
            Title = "Primary",
            MoreCommands =
            [
                new Separator("Group"),
                new CommandContextItem(new NoOpCommand { Name = "Secondary" }),
            ],
        };

        var viewModel = new CommandItemViewModel(new(item), new(pageContext), DefaultContextMenuFactory.Instance);
        viewModel.SlowInitializeProperties();

        Assert.IsTrue(viewModel.HasMoreCommands);
        Assert.IsNotNull(viewModel.SecondaryCommand);
        Assert.AreEqual("Secondary", viewModel.SecondaryCommand.Name);
    }

    [TestMethod]
    public void FastInitializeProperties_CreatesPrimaryContextItem()
    {
        // Context menus are opened from fast-initialized list items before slow init completes.
        // The synthetic primary command must already exist so the first right-click can open the menu.
        var pageContext = new TestPageContext();
        var item = new CommandItem(new NoOpCommand { Name = "Primary" })
        {
            Title = "Primary",
        };

        var viewModel = new CommandItemViewModel(new(item), new(pageContext), DefaultContextMenuFactory.Instance);
        viewModel.FastInitializeProperties();

        Assert.AreEqual(1, viewModel.AllCommands.Count);
        Assert.IsTrue(viewModel.CanOpenContextMenu);
        Assert.AreEqual("Primary", ((CommandContextItemViewModel)viewModel.AllCommands[0]).Name);
    }

    [TestMethod]
    public void LatePrimaryCommandCreation_AddsPrimaryToAllCommands()
    {
        // Reproduces issue where SlowInitializeProperties runs before a real primary command exists.
        // The late-arriving command should still create the synthetic primary context item and prepend it to AllCommands.
        var pageContext = new TestPageContext();
        var item = new CommandItem()
        {
            Command = null,
            MoreCommands =
            [
                new CommandContextItem(new NoOpCommand { Name = "Secondary" }),
            ],
        };

        var viewModel = new CommandItemViewModel(new(item), new(pageContext), DefaultContextMenuFactory.Instance);
        viewModel.SlowInitializeProperties();

        Assert.AreEqual(1, viewModel.AllCommands.Count);
        Assert.AreEqual("Secondary", ((CommandContextItemViewModel)viewModel.AllCommands[0]).Name);

        item.Command = new NoOpCommand { Name = "Primary" };

        Assert.AreEqual(2, viewModel.AllCommands.Count);
        Assert.AreEqual("Primary", ((CommandContextItemViewModel)viewModel.AllCommands[0]).Name);
        Assert.AreEqual("Secondary", ((CommandContextItemViewModel)viewModel.AllCommands[1]).Name);
        Assert.IsTrue(viewModel.HasMoreCommands);
        Assert.AreEqual("Secondary", viewModel.SecondaryCommand?.Name);
    }

    [TestMethod]
    public void SyntheticPrimaryContextItem_UpdatesSubtitleAndCachedSubtitleTarget()
    {
        // The synthetic primary context item copies subtitle state from the parent CommandItemViewModel.
        // When subtitle changes later, both the exposed subtitle and its cached fuzzy-search target must refresh.
        var pageContext = new TestPageContext();
        var item = new CommandItem(new NoOpCommand { Name = "Primary" })
        {
            Subtitle = "before",
            MoreCommands =
            [
                new CommandContextItem(new NoOpCommand { Name = "Secondary" }),
            ],
        };

        var viewModel = new CommandItemViewModel(new(item), new(pageContext), DefaultContextMenuFactory.Instance);
        viewModel.SlowInitializeProperties();

        var primaryContextItem = (CommandContextItemViewModel)viewModel.AllCommands[0];
        var matcher = new PrecomputedFuzzyMatcher(new PrecomputedFuzzyMatcherOptions());

        Assert.AreEqual("before", primaryContextItem.Subtitle);
        Assert.AreEqual("before", primaryContextItem.GetSubtitleTarget(matcher).Original);

        item.Subtitle = "after unique";

        Assert.AreEqual("after unique", primaryContextItem.Subtitle);
        Assert.AreEqual("after unique", primaryContextItem.GetSubtitleTarget(matcher).Original);
    }

    [TestMethod]
    public void InitializeAndCleanup_SubscribeAndRemoveOnProvidedScheduler()
    {
        using var scheduler = new GatedSingleThreadTaskScheduler(initiallyOpen: true);
        var pageContext = new TestPageContext(scheduler);
        var item = new TrackingCommandItem
        {
            Title = "Primary",
            Subtitle = "before",
            Command = new NoOpCommand { Name = "Primary" },
        };

        var viewModel = new CommandItemViewModel(new(item), new(pageContext), DefaultContextMenuFactory.Instance);
        viewModel.InitializeProperties();
        viewModel.SafeCleanup();

        Assert.AreEqual(1, item.AddCount);
        Assert.AreEqual(1, item.RemoveCount);
        Assert.AreEqual(scheduler.SchedulerThreadId, item.AddThreadId);
        Assert.AreEqual(scheduler.SchedulerThreadId, item.RemoveThreadId);
    }

    [TestMethod]
    public async Task CleanupRacingQueuedInitialize_DoesNotLeaveHandlerAttached()
    {
        using var scheduler = new GatedSingleThreadTaskScheduler();
        var pageContext = new TestPageContext(scheduler);
        var item = new TrackingCommandItem
        {
            Title = "Primary",
            Subtitle = "before",
            Command = new NoOpCommand { Name = "Primary" },
        };

        var viewModel = new CommandItemViewModel(new(item), new(pageContext), DefaultContextMenuFactory.Instance);
        var initializeTask = Task.Run(viewModel.InitializeProperties);

        Assert.IsTrue(scheduler.WaitForQueuedTaskCount(1, TimeSpan.FromSeconds(5)));

        var cleanupTask = Task.Run(viewModel.SafeCleanup);

        Assert.IsTrue(scheduler.WaitForQueuedTaskCount(2, TimeSpan.FromSeconds(5)));

        scheduler.Release();
        var initializationAndCleanup = Task.WhenAll(initializeTask, cleanupTask);
        var completedTask = await Task.WhenAny(initializationAndCleanup, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.AreSame(initializationAndCleanup, completedTask);
        Assert.AreEqual(0, item.AddCount);
        Assert.AreEqual(0, item.RemoveCount);

        item.Subtitle = "after";
        item.RaisePropertyChanged(nameof(ICommandItem.Subtitle));

        Assert.AreEqual("before", viewModel.Subtitle);
    }
}
