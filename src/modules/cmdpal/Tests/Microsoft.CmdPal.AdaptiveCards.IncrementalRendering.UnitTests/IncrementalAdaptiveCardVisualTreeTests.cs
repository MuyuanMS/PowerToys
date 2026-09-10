// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.AdaptiveCards.IncrementalRendering.UnitTests;

[TestClass]
public sealed class IncrementalAdaptiveCardVisualTreeTests
{
    [TestMethod]
    public void GetPlainTextPrefersSingleRun()
    {
        Assert.AreEqual(
            "run text",
            IncrementalAdaptiveCardVisualTree.GetPlainText("text property", "run text"));
    }

    [TestMethod]
    public void GetPlainTextUsesTextPropertyWithoutSingleRun()
    {
        Assert.AreEqual(
            "text property",
            IncrementalAdaptiveCardVisualTree.GetPlainText("text property", null));
    }
}
