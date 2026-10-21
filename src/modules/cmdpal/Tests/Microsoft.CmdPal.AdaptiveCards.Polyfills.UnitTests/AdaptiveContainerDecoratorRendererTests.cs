// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Data.Json;

namespace Microsoft.CmdPal.AdaptiveCards.Polyfills.UnitTests;

[TestClass]
public class AdaptiveContainerDecoratorRendererTests
{
    [TestMethod]
    [DataRow("""{ "roundedCorners": true, "showBorder": true }""", AdaptiveContainerDecoratorRenderer.RoundedBorderedStyleKey)]
    [DataRow("""{ "roundedCorners": true, "showBorder": false }""", AdaptiveContainerDecoratorRenderer.RoundedStyleKey)]
    [DataRow("""{ "roundedCorners": false, "showBorder": true }""", AdaptiveContainerDecoratorRenderer.BorderedStyleKey)]
    [DataRow("""{ "roundedCorners": false, "showBorder": false }""", null)]
    [DataRow("""{ "roundedCorners": "true", "showBorder": true }""", null)]
    [DataRow("""{ "roundedCorners": true, "showBorder": "true" }""", null)]
    [DataRow("""{ "showBorder": true }""", AdaptiveContainerDecoratorRenderer.BorderedStyleKey)]
    public void GetStyleKeySelectsTheMatchingDecoration(string propertiesJson, string? expectedStyleKey)
    {
        var properties = JsonObject.Parse(propertiesJson);

        Assert.AreEqual(expectedStyleKey, AdaptiveContainerDecoratorRenderer.GetStyleKey(properties));
    }
}
