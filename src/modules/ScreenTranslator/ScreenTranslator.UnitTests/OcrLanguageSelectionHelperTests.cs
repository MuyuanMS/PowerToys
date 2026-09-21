// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenTranslator.Core.Ocr;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.UnitTests;

[TestClass]
public class OcrLanguageSelectionHelperTests
{
    private static readonly string[] ExpectedPreferredThenPriorityCandidates = { "fr-FR", "ja-JP", "zh-Hans", "ko-KR" };
    private static readonly string[] ExpectedBoundedPreferredCandidates = { "fr-FR", "de-DE", "es-ES" };

    [TestMethod]
    public void ScoreRecognizedLines_FavorsJapaneseForPlausibleJapaneseText()
    {
        var lines = new List<TranslationLine>
        {
            new("日本語のサイトを翻訳します。これはテストです。", new PhysicalRect(0, 0, 240, 24), 0.95),
        };

        OcrLanguageScore japanese = OcrLanguageSelectionHelper.ScoreRecognizedLines("ja-JP", lines, 0);
        OcrLanguageScore english = OcrLanguageSelectionHelper.ScoreRecognizedLines("en-US", lines, 1);

        Assert.IsTrue(japanese.Score > english.Score);
    }

    [TestMethod]
    public void ScoreRecognizedLines_PenalizesReplacementAndMojibake()
    {
        var plausible = new List<TranslationLine>
        {
            new("こんにちは世界", new PhysicalRect(0, 0, 160, 24), 0.9),
        };
        var mojibake = new List<TranslationLine>
        {
            new("縺薙ｓ縺ｫ�世界", new PhysicalRect(0, 0, 160, 24), 0.9),
        };

        OcrLanguageScore plausibleScore = OcrLanguageSelectionHelper.ScoreRecognizedLines("ja-JP", plausible, 0);
        OcrLanguageScore mojibakeScore = OcrLanguageSelectionHelper.ScoreRecognizedLines("ja-JP", mojibake, 0);

        Assert.IsTrue(plausibleScore.Score > mojibakeScore.Score);
    }

    [TestMethod]
    public void ScoreRecognizedLines_StillHandlesLatinPages()
    {
        var lines = new List<TranslationLine>
        {
            new("PowerToys Screen Translator recognizes English text.", new PhysicalRect(0, 0, 360, 24), 0.95),
        };

        OcrLanguageScore english = OcrLanguageSelectionHelper.ScoreRecognizedLines("en-US", lines, 0);
        OcrLanguageScore japanese = OcrLanguageSelectionHelper.ScoreRecognizedLines("ja-JP", lines, 1);

        Assert.IsTrue(english.Score > japanese.Score);
    }

    [TestMethod]
    public void GetAutoLanguageCandidates_OrdersPreferredThenPriorityAndDeduplicates()
    {
        string[] installed = { "en-US", "ja-JP", "zh-Hans", "ko-KR", "fr-FR" };
        string[] preferred = { "fr-FR", "ja", "fr-FR" };

        IReadOnlyList<string> candidates = OcrLanguageSelectionHelper.GetAutoLanguageCandidates(
            installed,
            preferred,
            maxCandidateCount: 4);

        CollectionAssert.AreEqual(ExpectedPreferredThenPriorityCandidates, new List<string>(candidates));
    }

    [TestMethod]
    public void GetAutoLanguageCandidates_IsBounded()
    {
        string[] installed = { "en-US", "ja-JP", "zh-Hans", "zh-Hant", "ko-KR", "fr-FR", "de-DE", "es-ES" };
        string[] preferred = { "fr-FR", "de-DE", "es-ES", "ja-JP" };

        IReadOnlyList<string> candidates = OcrLanguageSelectionHelper.GetAutoLanguageCandidates(
            installed,
            preferred,
            maxCandidateCount: 3);

        Assert.HasCount(3, candidates);
        CollectionAssert.AreEqual(ExpectedBoundedPreferredCandidates, new List<string>(candidates));
    }

    [TestMethod]
    public void GetAutoLanguageCandidates_IncludesRemainingInstalledLanguagesWithinLimit()
    {
        string[] installed = { "en-US", "fr-FR", "de-DE" };
        string[] preferred = { "en-US" };

        IReadOnlyList<string> candidates = OcrLanguageSelectionHelper.GetAutoLanguageCandidates(
            installed,
            preferred);

        CollectionAssert.AreEqual(installed, new List<string>(candidates));
    }
}
