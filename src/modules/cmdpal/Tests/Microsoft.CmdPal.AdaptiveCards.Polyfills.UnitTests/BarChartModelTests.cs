// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.AdaptiveCards.Polyfills.UnitTests;

[TestClass]
public class BarChartModelTests
{
    [TestMethod]
    public void ParsesBars()
    {
        var warnings = new List<string>();
        var model = BarChartModel.Parse(
            """
            {
              "type": "Chart.VerticalBar",
              "title": "Cores",
              "xAxisTitle": "Core",
              "yAxisTitle": "% Utilization",
              "color": "categoricalTeal",
              "showBarValues": true,
              "yMin": 0,
              "yMax": 100,
              "data": [{ "x": "0", "y": 12 }, { "x": "1", "y": 87.5, "color": "attention" }, { "x": "2", "y": 40 }]
            }
            """,
            BarOrientation.Vertical,
            warnings);

        Assert.AreEqual(0, warnings.Count, string.Join(", ", warnings));
        Assert.AreEqual(BarOrientation.Vertical, model.Orientation);
        Assert.AreEqual("Cores", model.Title);
        Assert.IsFalse(model.ShowTitle);
        Assert.AreEqual("Core", model.XAxisTitle);
        Assert.AreEqual("% Utilization", model.YAxisTitle);
        Assert.AreEqual("categoricalTeal", model.Color);
        Assert.IsTrue(model.ShowBarValues);
        Assert.AreEqual(0d, model.YMin);
        Assert.AreEqual(100d, model.YMax);
        Assert.AreEqual(3, model.Data.Count);
        Assert.AreEqual("1", model.Data[1].Label);
        Assert.AreEqual("attention", model.Data[1].Color);
        Assert.AreEqual((12d, 87.5), model.GetValueExtent());
    }

    [TestMethod]
    public void EmptyDataHasNoExtent()
    {
        var (min, max) = new BarChartModel().GetValueExtent();

        Assert.IsTrue(double.IsNaN(min));
        Assert.IsTrue(double.IsNaN(max));
    }

    [TestMethod]
    public void EmptyDataUsesFiniteAxisRange()
    {
        var range = new BarChartModel { Orientation = BarOrientation.Vertical }.GetValueAxisRange();

        Assert.IsTrue(double.IsFinite(range.Min));
        Assert.IsTrue(double.IsFinite(range.Max));
        Assert.AreEqual(0, range.Min);
        Assert.AreEqual(1, range.Max);
    }

    [TestMethod]
    public void HorizontalAxisStartsAtZeroWhenDataContainsNegativeValues()
    {
        var model = BarChartModel.Parse(
            """{ "type": "Chart.HorizontalBar", "data": [{ "x": "negative", "y": -10 }, { "x": "positive", "y": 10 }] }""",
            BarOrientation.Horizontal,
            new List<string>());

        var range = model.GetValueAxisRange();

        Assert.AreEqual(0, range.Min);
        Assert.AreEqual(10, range.Max);
        Assert.AreEqual(0.5, range.Normalize(5));
    }

    [TestMethod]
    public void HorizontalAxisRemainsFiniteWhenAllDataIsNegative()
    {
        var model = BarChartModel.Parse(
            """{ "type": "Chart.HorizontalBar", "data": [{ "x": "one", "y": -10 }, { "x": "two", "y": -5 }] }""",
            BarOrientation.Horizontal,
            new List<string>());

        var range = model.GetValueAxisRange();

        Assert.IsTrue(double.IsFinite(range.Min));
        Assert.IsTrue(double.IsFinite(range.Max));
        Assert.AreEqual(0, range.Min);
        Assert.IsTrue(range.Max > 0);
        Assert.AreEqual(0, range.Normalize(0));
    }

    [TestMethod]
    public void HorizontalDisplayValueMatchesTheRenderedBar()
    {
        var horizontal = new BarChartModel { Orientation = BarOrientation.Horizontal };
        var vertical = new BarChartModel { Orientation = BarOrientation.Vertical };

        Assert.AreEqual(0, horizontal.GetDisplayValue(-5));
        Assert.AreEqual(5, horizontal.GetDisplayValue(5));
        Assert.AreEqual(-5, vertical.GetDisplayValue(-5));
    }

    [TestMethod]
    [DataRow("AbsoluteWithAxis", "AbsoluteWithAxis")]
    [DataRow("absoluteNoAxis", "AbsoluteNoAxis")]
    [DataRow("PartToWhole", "PartToWhole")]
    public void HorizontalBarsReadTheirDisplayMode(string displayMode, string expected)
    {
        var warnings = new List<string>();
        var model = BarChartModel.Parse(
            $$"""{ "type": "Chart.HorizontalBar", "displayMode": "{{displayMode}}", "showTitle": true, "data": [{ "x": "a", "y": 3 }, { "x": "b", "y": 1 }] }""",
            BarOrientation.Horizontal,
            warnings);

        Assert.AreEqual(0, warnings.Count, string.Join(", ", warnings));
        Assert.AreEqual(expected, model.DisplayMode.ToString());
        Assert.IsTrue(model.ShowTitle);
        Assert.AreEqual(4d, model.GetPositiveTotal());
    }

    [TestMethod]
    public void HorizontalBarsIgnoreVerticalBarProperties()
    {
        var warnings = new List<string>();
        var model = BarChartModel.Parse(
            """{ "type": "Chart.HorizontalBar", "showBarValues": true, "yMin": 0, "yMax": 100, "data": [] }""",
            BarOrientation.Horizontal,
            warnings);

        Assert.AreEqual(0, warnings.Count, string.Join(", ", warnings));
        Assert.IsFalse(model.ShowBarValues);
        Assert.IsNull(model.YMin);
        Assert.IsNull(model.YMax);
        Assert.AreEqual(BarDisplayMode.AbsoluteWithAxis, model.DisplayMode);
    }

    [TestMethod]
    public void UnknownDisplayModeWarns()
    {
        var warnings = new List<string>();
        var model = BarChartModel.Parse("""{ "type": "Chart.HorizontalBar", "displayMode": "stacked", "data": [] }""", BarOrientation.Horizontal, warnings);

        Assert.AreEqual(1, warnings.Count);
        Assert.AreEqual(BarDisplayMode.AbsoluteWithAxis, model.DisplayMode);
        Assert.AreEqual(BarOrientation.Horizontal, model.Orientation);
    }

    [TestMethod]
    public void MissingDataWarnsAndReturnsNoPoints()
    {
        var warnings = new List<string>();
        var model = BarChartModel.Parse("""{ "type": "Chart.VerticalBar" }""", BarOrientation.Vertical, warnings);

        Assert.AreEqual(1, warnings.Count);
        Assert.AreEqual("data is required.", warnings[0]);
        Assert.AreEqual(0, model.Data.Count);
    }

    [TestMethod]
    [DataRow(0, 0, 1)]
    [DataRow(1, 1, 0)]
    [DataRow(0.25, 0.25, 0.75)]
    public void HorizontalBarColumnWidthsPreserveZeroAndFullFractions(double fraction, double fill, double remainder)
    {
        Assert.AreEqual((fill, remainder), BarChartControl.GetBarColumnWidths(fraction));
    }

    [TestMethod]
    public void PartToWholeTooltipShowsShareAndValue()
    {
        Assert.AreEqual("25% (3)", BarChartControl.FormatTooltipValue(BarDisplayMode.PartToWhole, 0.25, 3));
    }

    [TestMethod]
    public void AbsoluteTooltipShowsValueOnly()
    {
        Assert.AreEqual("3", BarChartControl.FormatTooltipValue(BarDisplayMode.AbsoluteWithAxis, 0.25, 3));
    }
}
