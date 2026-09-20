// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.UnitTests;

[TestClass]
public class TranslationQualityGuardTests
{
    [TestMethod]
    public void ReplaceDegenerateTranslations_ReplacesRepeatedProviderOutput()
    {
        const string repeatedOutput = "(出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"出自\"";
        var sourceLine = new TranslatedLine(
            "Source attribution",
            repeatedOutput,
            new PhysicalRect(10, 20, 100, 30));
        var result = new TranslationResult(new List<TranslatedLine> { sourceLine });

        TranslationResult sanitized = TranslationQualityGuard.ReplaceDegenerateTranslations(result, out int replacementCount);

        Assert.AreEqual(1, replacementCount);
        Assert.AreEqual("Source attribution", sanitized.Lines[0].TranslatedText);
    }

    [TestMethod]
    public void ReplaceDegenerateTranslations_PreservesNormalTranslation()
    {
        var sourceLine = new TranslatedLine(
            "This is a longer sentence that should translate normally.",
            "这是一个应该正常翻译的较长句子，其中包含不同的词语、标点和自然变化。",
            new PhysicalRect(10, 20, 100, 30));
        var result = new TranslationResult(new List<TranslatedLine> { sourceLine });

        TranslationResult sanitized = TranslationQualityGuard.ReplaceDegenerateTranslations(result, out int replacementCount);

        Assert.AreEqual(0, replacementCount);
        Assert.AreSame(result, sanitized);
    }

    [TestMethod]
    public void ReplaceDegenerateTranslations_ReplacesDominantRepetitionEvenWhenSourceRepeats()
    {
        string source = string.Concat(System.Linq.Enumerable.Repeat("hello", 30));
        string translation = string.Concat(System.Linq.Enumerable.Repeat("你好", 30));
        var sourceLine = new TranslatedLine(source, translation, new PhysicalRect(10, 20, 100, 30));
        var result = new TranslationResult(new List<TranslatedLine> { sourceLine });

        TranslationResult sanitized = TranslationQualityGuard.ReplaceDegenerateTranslations(result, out int replacementCount);

        Assert.AreEqual(1, replacementCount);
        Assert.AreEqual(source, sanitized.Lines[0].TranslatedText);
    }
}
