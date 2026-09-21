// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.UnitTests;

[TestClass]
public class LanguageSelectionHelperTests
{
    [TestMethod]
    public void Swap_ExchangesExplicitEnglishAndChinese()
    {
        var (source, target) = LanguageSelectionHelper.Swap("en", "zh-Hans");

        Assert.AreEqual("zh-Hans", source);
        Assert.AreEqual("en-US", target);
    }

    [TestMethod]
    public void Swap_AutomaticEnglishTargetFallsBackToChinese()
    {
        var (source, target) = LanguageSelectionHelper.Swap("auto", "en-US");

        Assert.AreEqual("en-US", source);
        Assert.AreEqual("zh-Hans", target);
    }

    [TestMethod]
    public void Swap_AutomaticChineseTargetFallsBackToEnglish()
    {
        var (source, target) = LanguageSelectionHelper.Swap("auto", "zh-Hans");

        Assert.AreEqual("zh-Hans", source);
        Assert.AreEqual("en-US", target);
    }

    [TestMethod]
    public void AreEquivalent_MatchesRegionalLanguageTags()
    {
        Assert.IsTrue(LanguageSelectionHelper.AreEquivalent("en", "en-US"));
        Assert.IsTrue(LanguageSelectionHelper.AreEquivalent("zh", "zh-Hans"));
        Assert.IsFalse(LanguageSelectionHelper.AreEquivalent("en", "zh-Hans"));
    }
}
