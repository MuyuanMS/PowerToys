// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using CoreWidgetProvider.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.PerformanceMonitor.UnitTests;

[TestClass]
public class DataManagerTests
{
    [TestMethod]
    public void FirstRequestSamples()
    {
        Assert.IsTrue(DataManager.ShouldSample(new object(), 5_000));
    }

    [TestMethod]
    public void SecondTimerInTheSameSecondDoesNotSampleAgain()
    {
        var stats = new object();

        Assert.IsTrue(DataManager.ShouldSample(stats, 10_000));
        Assert.IsFalse(DataManager.ShouldSample(stats, 10_400));
        Assert.IsTrue(DataManager.ShouldSample(stats, 11_000));
        Assert.IsFalse(DataManager.ShouldSample(stats, 11_400));
    }

    [TestMethod]
    public void LateTimerTickStillSamples()
    {
        var stats = new object();

        Assert.IsTrue(DataManager.ShouldSample(stats, 20_000));
        Assert.IsTrue(DataManager.ShouldSample(stats, 21_200));
        Assert.IsTrue(DataManager.ShouldSample(stats, 22_000));
    }

    [TestMethod]
    public void EachStatsObjectHasItsOwnSchedule()
    {
        var cpu = new object();
        var memory = new object();

        Assert.IsTrue(DataManager.ShouldSample(cpu, 30_000));
        Assert.IsTrue(DataManager.ShouldSample(memory, 30_100));
    }

    [TestMethod]
    public void StopWaitsForAnImmediateUpdateAlreadyInProgress()
    {
        using var updateStarted = new ManualResetEventSlim();
        using var continueUpdate = new ManualResetEventSlim();
        using var manager = new DataManager(DataType.Memory, () =>
        {
            updateStarted.Set();
            continueUpdate.Wait();
        });

        try
        {
            manager.Start();
            Assert.IsTrue(updateStarted.Wait(TimeSpan.FromSeconds(5)));

            var stopTask = Task.Run(manager.Stop);
            Assert.IsFalse(stopTask.Wait(TimeSpan.FromMilliseconds(100)));

            continueUpdate.Set();
            Assert.IsTrue(stopTask.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            continueUpdate.Set();
            manager.Stop();
        }
    }
}
