// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file under the MIT license.
// See the LICENSE file in the project root for more information.

using CoreWidgetProvider.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.PerformanceMonitor.UnitTests;

[TestClass]
public class TemperatureStatsTests
{
    [DataTestMethod]
    [DataRow(2731.5, 0d)]
    [DataRow(2531.5, -20d)]
    [DataRow(4231.5, 150d)]
    public void CreateReading_ConvertsPlausibleRawValues(double raw, double expectedCelsius)
    {
        var reading = TemperatureStats.CreateReading(raw);

        Assert.IsTrue(reading.IsAvailable);
        Assert.IsTrue(reading.HasReading);
        Assert.AreEqual(expectedCelsius, reading.TemperatureCelsius, 0.0001d);
    }

    [DataTestMethod]
    [DataRow(2531.4)]
    [DataRow(4231.6)]
    public void CreateReading_RejectsOutOfRangeRawValues(double raw)
    {
        var reading = TemperatureStats.CreateReading(raw);

        Assert.IsFalse(reading.HasReading);
        Assert.AreEqual(-1d, reading.TemperatureCelsius);
    }

    [TestMethod]
    public void CreateReading_RejectsNaN()
    {
        var reading = TemperatureStats.CreateReading(double.NaN);

        Assert.IsFalse(reading.HasReading);
        Assert.AreEqual(-1d, reading.TemperatureCelsius);
    }
}
