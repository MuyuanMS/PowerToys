// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenTranslator.Core.Translation;

public static class TranslationQualityGuard
{
    private const int MinimumSuspiciousLength = 48;
    private const int MinimumRepeatCount = 8;
    private const int MaximumPatternLength = 12;

    public static TranslationResult ReplaceDegenerateTranslations(TranslationResult result, out int replacementCount)
    {
        replacementCount = 0;
        if (result.Lines.Count == 0)
        {
            return result;
        }

        List<TranslatedLine>? sanitizedLines = null;
        for (int i = 0; i < result.Lines.Count; i++)
        {
            TranslatedLine line = result.Lines[i];
            if (!IsLikelyDegenerate(line.OriginalText, line.TranslatedText))
            {
                sanitizedLines?.Add(line);
                continue;
            }

            sanitizedLines ??= CopyPrecedingLines(result.Lines, i);
            sanitizedLines.Add(line with { TranslatedText = line.OriginalText });
            replacementCount++;
        }

        return sanitizedLines is null
            ? result
            : result with { TranslatedLines = sanitizedLines };
    }

    public static bool IsLikelyDegenerate(string? sourceText, string? translatedText)
    {
        string source = Normalize(sourceText);
        string translation = Normalize(translatedText);
        if (translation.Length < MinimumSuspiciousLength)
        {
            return false;
        }

        bool severeExpansion = source.Length > 0 &&
                               translation.Length >= Math.Max(80, source.Length * 6);
        if (severeExpansion)
        {
            return true;
        }

        return HasDominantRepeatedPattern(translation);
    }

    private static List<TranslatedLine> CopyPrecedingLines(IReadOnlyList<TranslatedLine> lines, int count)
    {
        var copy = new List<TranslatedLine>(lines.Count);
        for (int i = 0; i < count; i++)
        {
            copy.Add(lines[i]);
        }

        return copy;
    }

    private static bool HasDominantRepeatedPattern(string text)
    {
        if (text.Length < MinimumSuspiciousLength)
        {
            return false;
        }

        int requiredCoverage = Math.Max(MinimumSuspiciousLength, (int)Math.Ceiling(text.Length * 0.6));
        int maximumPatternLength = Math.Min(MaximumPatternLength, text.Length / MinimumRepeatCount);

        for (int patternLength = 1; patternLength <= maximumPatternLength; patternLength++)
        {
            for (int start = 0; start + (patternLength * MinimumRepeatCount) <= text.Length; start++)
            {
                int repeatCount = 1;
                while (start + ((repeatCount + 1) * patternLength) <= text.Length &&
                       text.AsSpan(start, patternLength).SequenceEqual(
                           text.AsSpan(start + (repeatCount * patternLength), patternLength)))
                {
                    repeatCount++;
                }

                if (repeatCount >= MinimumRepeatCount &&
                    repeatCount * patternLength >= requiredCoverage)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (!char.IsWhiteSpace(character) && !char.IsControl(character))
            {
                normalized.Append(character);
            }
        }

        return normalized.ToString();
    }
}
