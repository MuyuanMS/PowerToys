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

    [TestMethod]
    public void ResolveAutomaticChineseEnglishTarget_ChineseTextChoosesEnglish()
    {
        TranslationLine[] lines =
        [
            new("受影响订单", new PhysicalRect(0, 0, 100, 20)),
            new("支付失败率上升", new PhysicalRect(0, 30, 140, 20)),
        ];

        string target = LanguageSelectionHelper.ResolveAutomaticChineseEnglishTarget(
            lines,
            "auto",
            "zh-Hans");

        Assert.AreEqual("en-US", target);
    }

    [TestMethod]
    public void ResolveAutomaticChineseEnglishTarget_EnglishTextChoosesChinese()
    {
        TranslationLine[] lines =
        [
            new("Affected orders", new PhysicalRect(0, 0, 100, 20)),
            new("Payment failure rate increased", new PhysicalRect(0, 30, 180, 20)),
        ];

        string target = LanguageSelectionHelper.ResolveAutomaticChineseEnglishTarget(
            lines,
            "auto",
            "en-US");

        Assert.AreEqual("zh-Hans", target);
    }

    [TestMethod]
    public void ResolveAutomaticChineseEnglishTarget_ExplicitSourcePreservesTarget()
    {
        TranslationLine[] lines =
        [
            new("Affected orders", new PhysicalRect(0, 0, 100, 20)),
        ];

        string target = LanguageSelectionHelper.ResolveAutomaticChineseEnglishTarget(
            lines,
            "en",
            "en-US");

        Assert.AreEqual("en-US", target);
    }

    [TestMethod]
    public void ResolveAutomaticChineseEnglishTarget_JapaneseTextPreservesTarget()
    {
        TranslationLine[] lines =
        [
            new("注文を確認してください", new PhysicalRect(0, 0, 160, 20)),
        ];

        string target = LanguageSelectionHelper.ResolveAutomaticChineseEnglishTarget(
            lines,
            "auto",
            "en-US");

        Assert.AreEqual("en-US", target);
    }

    [TestMethod]
    public void ResolveAutomaticChineseEnglishTarget_ChineseDominantMixedTextChoosesEnglish()
    {
        TranslationLine[] lines =
        [
            new("Contoso 全球支付中心", new PhysicalRect(0, 0, 180, 20)),
            new("支付失败率上升", new PhysicalRect(0, 30, 140, 20)),
        ];

        string target = LanguageSelectionHelper.ResolveAutomaticChineseEnglishTarget(
            lines,
            "auto",
            "zh-Hans");

        Assert.AreEqual("en-US", target);
    }

    [TestMethod]
    public void ResolveAutomaticChineseEnglishTarget_OtherTargetPreservesSelection()
    {
        TranslationLine[] lines =
        [
            new("Affected orders", new PhysicalRect(0, 0, 100, 20)),
        ];

        string target = LanguageSelectionHelper.ResolveAutomaticChineseEnglishTarget(
            lines,
            "auto",
            "fr");

        Assert.AreEqual("fr", target);
    }
}
