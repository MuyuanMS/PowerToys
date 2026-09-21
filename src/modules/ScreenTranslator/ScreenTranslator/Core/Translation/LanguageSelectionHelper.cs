// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

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

    private static bool IsAutomatic(string language)
    {
        return string.IsNullOrWhiteSpace(language) ||
            string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase);
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
