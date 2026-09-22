// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace ScreenTranslator.Core.Translation;

public static class LanguageSelectionHelper
{
    public static (string SourceLanguage, string TargetLanguage) Swap(
        string sourceLanguage,
        string targetLanguage)
    {
        string newSourceLanguage = string.IsNullOrWhiteSpace(targetLanguage)
            ? "en-US"
            : targetLanguage;
        string newTargetLanguage = IsAutomatic(sourceLanguage)
            ? GetAutomaticSwapTarget(targetLanguage)
            : NormalizeTargetLanguage(sourceLanguage);

        return (newSourceLanguage, newTargetLanguage);
    }

    public static bool AreEquivalent(string firstLanguage, string secondLanguage)
    {
        if (string.IsNullOrWhiteSpace(firstLanguage) || string.IsNullOrWhiteSpace(secondLanguage))
        {
            return false;
        }

        string firstBase = GetBaseLanguage(firstLanguage);
        string secondBase = GetBaseLanguage(secondLanguage);
        return string.Equals(firstBase, secondBase, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveAutomaticChineseEnglishTarget(
        IReadOnlyList<TranslationLine> lines,
        string sourceLanguage,
        string targetLanguage)
    {
        if (!IsAutomatic(sourceLanguage) ||
            lines == null ||
            lines.Count == 0 ||
            !IsChineseEnglishTarget(targetLanguage))
        {
            return targetLanguage;
        }

        int hanCount = 0;
        int latinCount = 0;
        int kanaCount = 0;
        int hangulCount = 0;
        foreach (char character in string.Concat(lines.Select(line => line.Text)))
        {
            if (IsHan(character))
            {
                hanCount++;
            }
            else if (IsLatin(character))
            {
                latinCount++;
            }
            else if (IsKana(character))
            {
                kanaCount++;
            }
            else if (IsHangul(character))
            {
                hangulCount++;
            }
        }

        if (kanaCount > 0 || hangulCount > 0)
        {
            return targetLanguage;
        }

        if (hanCount >= 2 && hanCount * 2 >= latinCount)
        {
            return "en-US";
        }

        if (latinCount >= 2 && latinCount > hanCount * 2)
        {
            return "zh-Hans";
        }

        return targetLanguage;
    }

    private static bool IsAutomatic(string language)
    {
        return string.IsNullOrWhiteSpace(language) ||
            string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChineseEnglishTarget(string language)
    {
        string baseLanguage = GetBaseLanguage(language);
        return string.Equals(baseLanguage, "en", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(baseLanguage, "zh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHan(char character)
    {
        return character is >= '\u3400' and <= '\u4DBF' ||
            character is >= '\u4E00' and <= '\u9FFF' ||
            character is >= '\uF900' and <= '\uFAFF';
    }

    private static bool IsLatin(char character)
    {
        return character is >= 'A' and <= 'Z' || character is >= 'a' and <= 'z';
    }

    private static bool IsKana(char character)
    {
        return character is >= '\u3040' and <= '\u30FF';
    }

    private static bool IsHangul(char character)
    {
        return character is >= '\uAC00' and <= '\uD7AF';
    }

    private static string GetAutomaticSwapTarget(string previousTargetLanguage)
    {
        return string.Equals(GetBaseLanguage(previousTargetLanguage), "en", StringComparison.OrdinalIgnoreCase)
            ? "zh-Hans"
            : "en-US";
    }

    private static string NormalizeTargetLanguage(string language)
    {
        return GetBaseLanguage(language).ToLowerInvariant() switch
        {
            "en" => "en-US",
            "zh" => "zh-Hans",
            _ => language,
        };
    }

    private static string GetBaseLanguage(string language)
    {
        int separatorIndex = language.IndexOf('-');
        return separatorIndex > 0 ? language[..separatorIndex] : language;
    }
}
