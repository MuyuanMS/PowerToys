// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;

namespace Microsoft.CmdPal.AdaptiveCards.Polyfills.UnitTests;

[TestClass]
public class ChartFormatTests
{
    private static readonly CompositeFormat SeriesNameFallback = CompositeFormat.Parse("Series {0}");
    private static readonly CompositeFormat SeriesSummaryFallback = CompositeFormat.Parse("{0}: latest {1}, low {2}, high {3}.");

    [TestMethod]
    public void ValidLocalizedFormatIsUsed()
    {
        var format = ChartFormat.Load("Reihe {0}", SeriesNameFallback, argumentCount: 1);

        Assert.AreEqual("Reihe 2", string.Format(format, 2));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Series {")]
    [DataRow("Series")]
    [DataRow("Series {1}")]
    public void MissingOrMalformedFormatUsesFallback(string value)
    {
        var format = ChartFormat.Load(value, SeriesNameFallback, argumentCount: 1);

        Assert.AreEqual("Series 2", string.Format(format, 2));
    }

    [TestMethod]
    public void SummaryFormatMustAcceptAllArguments()
    {
        var format = ChartFormat.Load("{0}: latest {1}, low {2}, high {3}.", SeriesSummaryFallback, argumentCount: 4);

        Assert.AreEqual("Sales: latest 12, low 3, high 20.", string.Format(format, "Sales", 12, 3, 20));
        Assert.AreSame(SeriesSummaryFallback, ChartFormat.Load("{0}: latest {1}.", SeriesSummaryFallback, argumentCount: 4));
    }
}
