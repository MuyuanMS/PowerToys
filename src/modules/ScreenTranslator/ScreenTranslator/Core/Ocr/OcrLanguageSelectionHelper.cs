// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Core.Ocr;

public static class OcrLanguageSelectionHelper
{
    public const int MaxAutoCandidateCount = 8;

    private static readonly string[] PriorityLanguageTags =
    {
        "ja-JP",
        "ja",
        "zh-Hans",
        "zh-CN",
        "zh-Hant",
        "zh-TW",
        "ko-KR",
        "ko",
        "en-US",
        "en",
    };

    private static readonly char[] LanguageTagSeparators = { '-', '_' };

    public static IReadOnlyList<string> GetAutoLanguageCandidates(
        IEnumerable<string> installedLanguageTags,
        IEnumerable<string>? preferredLanguageTags,
        int maxCandidateCount = MaxAutoCandidateCount)
    {
        if (maxCandidateCount <= 0)
        {
            return Array.Empty<string>();
        }

        List<string> installed = installedLanguageTags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        if (installed.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> candidates = new();
        void AddCandidate(string? requestedTag)
        {
            string? installedTag = ResolveInstalledTag(installed, requestedTag);
            if (installedTag != null &&
                !candidates.Contains(installedTag, StringComparer.OrdinalIgnoreCase) &&
                candidates.Count < maxCandidateCount)
            {
                candidates.Add(installedTag);
            }
        }

        if (preferredLanguageTags != null)
        {
            foreach (string preferred in preferredLanguageTags)
            {
                AddCandidate(preferred);
            }
        }

        foreach (string priority in PriorityLanguageTags)
        {
            AddCandidate(priority);
        }

        foreach (string installedTag in installed)
        {
            AddCandidate(installedTag);
        }

        return candidates;
    }

    public static OcrLanguageScore ScoreRecognizedLines(
        string languageTag,
        IReadOnlyList<TranslationLine> lines,
        int candidateOrder)
    {
        if (lines == null || lines.Count == 0)
        {
            return new OcrLanguageScore(languageTag, double.MinValue + candidateOrder, 0, candidateOrder);
        }

        string text = string.Join(" ", lines.Select(line => line.Text));
        int nonWhitespace = 0;
        int latin = 0;
        int hiragana = 0;
        int katakana = 0;
        int han = 0;
        int hangul = 0;
        int replacement = 0;
        int control = 0;
        int mojibake = 0;
        int punctuationOrSymbol = 0;

        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            nonWhitespace++;
            if (IsLatin(character))
            {
                latin++;
            }
            else if (IsHiragana(character))
            {
                hiragana++;
            }
            else if (IsKatakana(character))
            {
                katakana++;
            }
            else if (IsHan(character))
            {
                han++;
            }
            else if (IsHangul(character))
            {
                hangul++;
            }

            if (character == '\uFFFD')
            {
                replacement++;
            }

            if (char.IsControl(character))
            {
                control++;
            }

            if (IsMojibakeLike(character))
            {
                mojibake++;
            }

            if (char.IsPunctuation(character) || char.IsSymbol(character))
            {
                punctuationOrSymbol++;
            }
        }

        if (nonWhitespace == 0)
        {
            return new OcrLanguageScore(languageTag, double.MinValue + candidateOrder, 0, candidateOrder);
        }

        double averageConfidence = Math.Clamp(lines.Average(line => line.Confidence), 0.0, 1.0);
        double score = nonWhitespace + (lines.Count * 2.0) + (averageConfidence * 12.0);

        string primaryLanguage = GetPrimaryLanguage(languageTag);
        int kana = hiragana + katakana;
        int cjk = kana + han + hangul;
        score += primaryLanguage switch
        {
            "ja" => (hiragana * 6.0) + (katakana * 5.0) + (han * 2.2) - (hangul * 2.5),
            "zh" => (han * 4.0) - (kana * 2.0) - (hangul * 2.0),
            "ko" => (hangul * 5.0) - (kana * 1.5),
            "en" => (latin * 2.0) - (cjk * 0.5),
            _ => latin + cjk,
        };

        if (primaryLanguage == "ja" && kana > 0 && han > 0)
        {
            score += 20.0;
        }
        else if (primaryLanguage == "ja" && kana > 0)
        {
            score += 12.0;
        }

        if (primaryLanguage is "ja" or "zh" or "ko" && cjk == 0 && latin > nonWhitespace * 0.8)
        {
            score -= 8.0;
        }

        score -= replacement * 20.0;
        score -= control * 8.0;
        score -= mojibake * 5.0;
        if (punctuationOrSymbol > nonWhitespace * 0.45)
        {
            score -= punctuationOrSymbol * 1.5;
        }

        return new OcrLanguageScore(languageTag, score, nonWhitespace, candidateOrder);
    }

    public static OcrLanguageScore SelectBestScore(IEnumerable<OcrLanguageScore> scores)
    {
        return scores
            .OrderByDescending(score => score.Score)
            .ThenByDescending(score => score.RecognizedCharacterCount)
            .ThenBy(score => score.CandidateOrder)
            .First();
    }

    private static string? ResolveInstalledTag(IReadOnlyList<string> installedLanguageTags, string? requestedTag)
    {
        if (string.IsNullOrWhiteSpace(requestedTag))
        {
            return null;
        }

        string trimmed = requestedTag.Trim();
        string? exact = installedLanguageTags.FirstOrDefault(
            tag => string.Equals(tag, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        string primary = GetPrimaryLanguage(trimmed);
        return installedLanguageTags.FirstOrDefault(
            tag => string.Equals(GetPrimaryLanguage(tag), primary, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetPrimaryLanguage(string languageTag)
    {
        int separator = languageTag.IndexOfAny(LanguageTagSeparators);
        return (separator > 0 ? languageTag[..separator] : languageTag).ToLowerInvariant();
    }

    private static bool IsLatin(char character)
    {
        return (character >= 'A' && character <= 'Z') ||
               (character >= 'a' && character <= 'z') ||
               (character >= '\u00C0' && character <= '\u024F');
    }

    private static bool IsHiragana(char character)
    {
        return character >= '\u3040' && character <= '\u309F';
    }

    private static bool IsKatakana(char character)
    {
        return (character >= '\u30A0' && character <= '\u30FF') ||
               (character >= '\uFF66' && character <= '\uFF9D');
    }

    private static bool IsHan(char character)
    {
        return (character >= '\u3400' && character <= '\u4DBF') ||
               (character >= '\u4E00' && character <= '\u9FFF') ||
               (character >= '\uF900' && character <= '\uFAFF');
    }

    private static bool IsHangul(char character)
    {
        return (character >= '\uAC00' && character <= '\uD7AF') ||
               (character >= '\u1100' && character <= '\u11FF') ||
               (character >= '\u3130' && character <= '\u318F');
    }

    private static bool IsMojibakeLike(char character)
    {
        return character is 'Ã' or 'ã' or 'Â' or 'â' or '�' or '縺' or '譁' or '繧' or '荳';
    }
}

public readonly record struct OcrLanguageScore(
    string LanguageTag,
    double Score,
    int RecognizedCharacterCount,
    int CandidateOrder);
