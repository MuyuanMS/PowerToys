// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public sealed class StringParameterRunViewModelTests
{
    [TestMethod]
    public void ModelReset_CancelsPendingUiWrite()
    {
        var model = new StringParameterRun();
        model.Text = "previous";
        var scheduler = new QueuedTaskScheduler();
        var context = new TestPageContext();
        using var viewModel = new StringParameterRunViewModel(
            model,
            new WeakReference<IPageContext>(context),
            scheduler);
        viewModel.InitializeProperties();

        viewModel.SetTextFromUi("42");
        model.ClearValue();
        scheduler.RunQueuedTasks();

        Assert.AreEqual(string.Empty, model.Text);
        Assert.AreEqual(string.Empty, viewModel.TextForUI);
    }

    private sealed class TestPageContext : IPageContext
    {
        public TaskScheduler Scheduler => TaskScheduler.Default;

        public ICommandProviderContext ProviderContext => CommandProviderContext.Empty;

        public void ShowException(Exception ex, string? extensionHint = null) =>
            throw new AssertFailedException($"Unexpected exception from view model: {ex}");
    }

    private sealed class QueuedTaskScheduler : TaskScheduler
    {
        private readonly ConcurrentQueue<Task> _tasks = new();

        protected override IEnumerable<Task>? GetScheduledTasks() => _tasks.ToArray();

        protected override void QueueTask(Task task) => _tasks.Enqueue(task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

        public void RunQueuedTasks()
        {
            while (_tasks.TryDequeue(out var task))
            {
                TryExecuteTask(task);
            }
        }
    }
}
