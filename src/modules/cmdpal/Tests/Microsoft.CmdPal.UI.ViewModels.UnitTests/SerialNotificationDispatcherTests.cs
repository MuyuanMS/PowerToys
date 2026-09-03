// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CmdPal.UI.ViewModels.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

/// <summary>
/// Verifies the single ordered dispatch path for provider add/remove notifications
/// (r3-p4-04). Every emission runs on one worker in strict first-in-first-out order, so a
/// consumer can never observe a provider addition ahead of the removal enqueued before it,
/// even when the two originate on different threads, and none of this depends on a
/// UI-thread concept such as DispatcherQueue.
/// </summary>
[TestClass]
public class SerialNotificationDispatcherTests
{
    private static readonly string[] ExpectedAsyncOrderBeforeRelease = ["first-start"];
    private static readonly string[] ExpectedAsyncOrderAfterRelease = ["first-start", "first-end", "second"];

    [TestMethod]
    public void Enqueue_RunsNotificationsInFifoOrder()
    {
        using var dispatcher = new SerialNotificationDispatcher();
        var observed = new ConcurrentQueue<int>();
        var done = new CountdownEvent(500);

        for (var i = 0; i < 500; i++)
        {
            var value = i;
            dispatcher.Enqueue(() =>
            {
                observed.Enqueue(value);
                done.Signal();
            });
        }

        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(5)), "All notifications should have run.");

        var expected = 0;
        foreach (var value in observed)
        {
            Assert.AreEqual(expected, value, "Notifications must run in enqueue order.");
            expected++;
        }

        Assert.AreEqual(500, expected);
    }

    // A paired removal enqueued ahead of an addition must always be observed first, even
    // when the two are enqueued from different threads racing each other.
    [TestMethod]
    public void Enqueue_FromConcurrentThreads_PreservesPerCallerOrder()
    {
        using var dispatcher = new SerialNotificationDispatcher();
        var orderViolations = 0;
        var callers = new Task[100];
        var done = new CountdownEvent(200);
        using var start = new ManualResetEventSlim();

        for (var i = 0; i < 100; i++)
        {
            callers[i] = Task.Run(() =>
            {
                start.Wait();
                var removed = false;
                dispatcher.Enqueue(() =>
                {
                    removed = true;
                    done.Signal();
                });
                dispatcher.Enqueue(() =>
                {
                    if (!removed)
                    {
                        Interlocked.Increment(ref orderViolations);
                    }

                    done.Signal();
                });
            });
        }

        start.Set();
        Task.WaitAll(callers);
        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(5)), "All notifications should have run.");
        Assert.AreEqual(0, Volatile.Read(ref orderViolations), "An addition must never overtake the removal enqueued before it.");
    }

    [TestMethod]
    public void Enqueue_AfterDispose_IsDroppedSilently()
    {
        var dispatcher = new SerialNotificationDispatcher();
        dispatcher.Dispose();

        var ran = false;
        dispatcher.Enqueue(() => ran = true);

        Thread.Sleep(100);
        Assert.IsFalse(ran, "A notification enqueued after dispose must not run.");
    }

    [TestMethod]
    public void Dispose_DrainsAlreadyEnqueuedNotifications()
    {
        var dispatcher = new SerialNotificationDispatcher();
        var count = 0;

        for (var i = 0; i < 50; i++)
        {
            dispatcher.Enqueue(() => Interlocked.Increment(ref count));
        }

        dispatcher.Dispose();

        Assert.AreEqual(50, Volatile.Read(ref count), "Dispose must let already-queued notifications drain.");
    }

    [TestMethod]
    public void Dispose_WaitsUntilAlreadyEnqueuedNotificationCompletes()
    {
        var dispatcher = new SerialNotificationDispatcher();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var count = 0;

        dispatcher.Enqueue(() =>
        {
            entered.Set();
            release.Wait();
            Interlocked.Increment(ref count);
        });
        dispatcher.Enqueue(() => Interlocked.Increment(ref count));

        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)), "The first notification should start.");

        var dispose = Task.Run(dispatcher.Dispose);
        Thread.Sleep(100);
        Assert.IsFalse(dispose.IsCompleted, "Dispose must wait for the queue to drain rather than timing out.");

        release.Set();
        Assert.IsTrue(dispose.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(2, Volatile.Read(ref count));
    }

    [TestMethod]
    public void Enqueue_AwaitsAsyncNotificationBeforeNextNotification()
    {
        using var dispatcher = new SerialNotificationDispatcher();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new ConcurrentQueue<string>();
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        dispatcher.Enqueue(async () =>
        {
            order.Enqueue("first-start");
            await release.Task.ConfigureAwait(false);
            order.Enqueue("first-end");
        });
        dispatcher.Enqueue(() =>
        {
            order.Enqueue("second");
            secondRan.SetResult();
        });

        Thread.Sleep(100);
        CollectionAssert.AreEqual(ExpectedAsyncOrderBeforeRelease, order.ToArray());

        release.SetResult();

        Assert.IsTrue(secondRan.Task.Wait(TimeSpan.FromSeconds(5)), "The next notification should run after the async work completes.");
        CollectionAssert.AreEqual(ExpectedAsyncOrderAfterRelease, order.ToArray());
    }

    [TestMethod]
    public void Enqueue_HandlerException_DoesNotStopLaterNotifications()
    {
        using var dispatcher = new SerialNotificationDispatcher();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        dispatcher.Enqueue(() => throw new InvalidOperationException("boom"));
        dispatcher.Enqueue(() => reached.TrySetResult());

        Assert.IsTrue(reached.Task.Wait(TimeSpan.FromSeconds(5)), "A throwing handler must not stall the worker.");
    }

    // The dispatcher must never rely on a UI-thread concept such as DispatcherQueue: it has
    // to keep running notifications even when the calling thread's SynchronizationContext
    // would reject any attempt to marshal work onto it.
    [TestMethod]
    public void Enqueue_DoesNotDependOnCallingThreadSynchronizationContext()
    {
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new ThrowingSynchronizationContext());

            using var dispatcher = new SerialNotificationDispatcher();
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            dispatcher.Enqueue(() => reached.TrySetResult());

            Assert.IsTrue(reached.Task.Wait(TimeSpan.FromSeconds(5)), "Notifications must run without depending on the caller's synchronization context.");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private sealed class ThrowingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("The dispatcher must not marshal work through the caller's synchronization context.");

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("The dispatcher must not marshal work through the caller's synchronization context.");
    }
}
