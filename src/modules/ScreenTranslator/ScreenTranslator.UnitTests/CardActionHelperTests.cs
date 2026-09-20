// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenTranslator.Core.Actions;

namespace ScreenTranslator.UnitTests;

[TestClass]
public class CardActionHelperTests
{
    [TestMethod]
    public void CreateSearchUri_EscapesSearchText()
    {
        var uri = CardActionHelper.CreateSearchUri("hello 世界");

        Assert.AreEqual("https://www.bing.com/search?q=hello%20%E4%B8%96%E7%95%8C", uri.AbsoluteUri);
    }

    [TestMethod]
    public void CreateBingTranslatorUri_UsesOriginalTextAndTargetLanguage()
    {
        var uri = CardActionHelper.CreateBingTranslatorUri("hello 世界 & friends", "zh-Hans");

        Assert.AreEqual(
            "https://www.bing.com/translator?from=auto-detect&to=zh-Hans&text=hello%20%E4%B8%96%E7%95%8C%20%26%20friends",
            uri.AbsoluteUri);
    }

    [TestMethod]
    public void TryGetWebUri_DetectsOnlyWholeWebAddresses()
    {
        Assert.IsTrue(CardActionHelper.TryGetWebUri("example.com/path", out var uri));
        Assert.AreEqual("https://example.com/path", uri!.AbsoluteUri);
        Assert.IsFalse(CardActionHelper.TryGetWebUri("Visit example.com", out _));
    }

    [TestMethod]
    public void TryGetEmailAddress_DetectsOnlyWholeEmailAddresses()
    {
        Assert.IsTrue(CardActionHelper.TryGetEmailAddress("person@example.com", out var emailAddress));
        Assert.AreEqual("person@example.com", emailAddress);
        Assert.IsFalse(CardActionHelper.TryGetEmailAddress("Email person@example.com", out _));
    }
}
