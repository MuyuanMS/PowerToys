// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CoreWidgetProvider.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.PerformanceMonitor.UnitTests;

[TestClass]
public class DiskStatsTests
{
    [TestMethod]
    public void ResponseTimeIsUnknownWhenItsOptionalCounterIsUnavailable()
    {
        var data = new DiskStats.Data();

        Assert.IsNull(data.ResponseTime);

        data.ResponseTime = 0.001f;
        Assert.AreEqual(0.001f, data.ResponseTime.Value);
    }

    [TestMethod]
    public void ResponseTimeIsUnknownWhenTheReadingIsNotFinite()
    {
        Assert.IsNull(DiskStats.GetResponseTime(float.NaN));
        Assert.IsNull(DiskStats.GetResponseTime(float.PositiveInfinity));
        Assert.IsNull(DiskStats.GetResponseTime(float.NegativeInfinity));
    }

    [TestMethod]
    public void ResponseTimeClampsFiniteNegativeReadingsToZero()
    {
        Assert.AreEqual(0f, DiskStats.GetResponseTime(-0.001f).Value);
        Assert.AreEqual(0.001f, DiskStats.GetResponseTime(0.001f).Value);
    }

    [TestMethod]
    [DataRow(100f, 0f)]
    [DataRow(75f, 25f)]
    [DataRow(2.5f, 97.5f)]
    [DataRow(0f, 100f)]
    public void ActiveTimeIsTheTimeTheDiskWasNotIdle(float idlePercent, float expected)
    {
        Assert.AreEqual(expected, DiskStats.GetActiveTimePercent(idlePercent, hasPreviousSample: true), 0.001f);
    }

    [TestMethod]
    public void ActiveTimeStaysBetweenZeroAndOneHundred()
    {
        Assert.AreEqual(0f, DiskStats.GetActiveTimePercent(104.2f, hasPreviousSample: true));
        Assert.AreEqual(100f, DiskStats.GetActiveTimePercent(-3f, hasPreviousSample: true));
    }

    [TestMethod]
    public void ActiveTimeIsZeroWhenTheReadingIsNotANumber()
    {
        Assert.AreEqual(0f, DiskStats.GetActiveTimePercent(float.NaN, hasPreviousSample: true));
        Assert.AreEqual(0f, DiskStats.GetActiveTimePercent(float.PositiveInfinity, hasPreviousSample: true));
        Assert.AreEqual(0f, DiskStats.GetActiveTimePercent(float.NegativeInfinity, hasPreviousSample: true));
    }

    [TestMethod]
    public void FirstReadingIsNotShownAsBusy()
    {
        // NextValue() returns 0 before the counter has a previous sample.
        Assert.AreEqual(0f, DiskStats.GetActiveTimePercent(0f, hasPreviousSample: false));
        Assert.AreEqual(0f, DiskStats.GetActiveTimePercent(40f, hasPreviousSample: false));
    }
}
