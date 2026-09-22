// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Core.Layout;

internal static class ScoredTextBlockGrouper
{
    private const double MergeThreshold = 5.5;

    public static IReadOnlyList<TranslationLine> Group(IEnumerable<TranslationLine> lines)
    {
        if (lines == null)
        {
            return Array.Empty<TranslationLine>();
        }

        List<TranslationLine> ordered = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text) && !line.BoundingBox.IsEmpty)
            .OrderBy(line => line.BoundingBox.Top)
            .ThenBy(line => line.BoundingBox.Left)
            .ToList();
        if (ordered.Count < 2)
        {
            return ordered;
        }

        ordered = OverlayLayoutHelper.MergeSameBaselineFragments(ordered);
        List<List<TranslationLine>> groups = new();
        foreach (TranslationLine line in ordered)
        {
            List<TranslationLine>? bestGroup = null;
            double bestScore = double.MinValue;
            foreach (List<TranslationLine> group in groups)
            {
                double score = ScorePair(group[^1], line);
                if (score >= MergeThreshold && score > bestScore)
                {
                    bestGroup = group;
                    bestScore = score;
                }
            }

            if (bestGroup is null)
            {
                groups.Add(new List<TranslationLine> { line });
            }
            else
            {
                bestGroup.Add(line);
            }
        }

        return groups.Select(OverlayLayoutHelper.MergeTextLineGroup).ToList();
    }

    private static double ScorePair(TranslationLine previous, TranslationLine current)
    {
        double previousHeight = GetLineHeight(previous);
        double currentHeight = GetLineHeight(current);
        double referenceHeight = Math.Max(1.0, (previousHeight + currentHeight) / 2.0);
        double heightRatio = Math.Min(previousHeight, currentHeight) / Math.Max(previousHeight, currentHeight);
        double verticalGapRatio = (current.BoundingBox.Top - previous.BoundingBox.Bottom) / referenceHeight;
        if (heightRatio < 0.75 || verticalGapRatio < -0.25 || verticalGapRatio > 0.85)
        {
            return double.MinValue;
        }

        double overlapRight = Math.Min(previous.BoundingBox.Right, current.BoundingBox.Right);
        double overlapLeft = Math.Max(previous.BoundingBox.Left, current.BoundingBox.Left);
        double overlap = Math.Max(0, overlapRight - overlapLeft);
        double overlapRatio = overlap / Math.Max(1.0, Math.Min(previous.BoundingBox.Width, current.BoundingBox.Width));
        double leftDeltaRatio = Math.Abs(previous.BoundingBox.Left - current.BoundingBox.Left) / referenceHeight;
        if (overlapRatio < 0.35 && leftDeltaRatio > 1.0)
        {
            return double.MinValue;
        }

        double score = 0;
        score += heightRatio >= 0.9 ? 2.0 : 1.0;
        score += verticalGapRatio <= 0.35 ? 2.0 : verticalGapRatio <= 0.65 ? 1.0 : 0;
        score += leftDeltaRatio <= 0.25 ? 2.0 : leftDeltaRatio <= 0.75 ? 1.0 : 0;
        score += overlapRatio >= 0.8 ? 1.5 : overlapRatio >= 0.5 ? 0.5 : 0;

        double widthRatio = Math.Min(previous.BoundingBox.Width, current.BoundingBox.Width) /
                            Math.Max(previous.BoundingBox.Width, current.BoundingBox.Width);
        if (widthRatio >= 0.65)
        {
            score += 0.5;
        }

        string previousText = previous.Text.Trim();
        string currentText = current.Text.Trim();
        if (previousText.EndsWith('-'))
        {
            score += 2.0;
        }

        if (EndsLogicalStatement(previousText))
        {
            score -= 3.0;
        }

        if (StartsIndependentItem(currentText))
        {
            score -= 3.0;
        }

        return score;
    }

    private static double GetLineHeight(TranslationLine line)
    {
        return line.BoundingBox.Height / Math.Max(1, line.SourceLineCount);
    }

    private static bool EndsLogicalStatement(string text)
    {
        return text.EndsWith('.') ||
               text.EndsWith('!') ||
               text.EndsWith('?') ||
               text.EndsWith('。') ||
               text.EndsWith('！') ||
               text.EndsWith('？');
    }

    private static bool StartsIndependentItem(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        char first = text[0];
        return first is '•' or '·' or '▪' or '◦' or '>' or '$' or '#' ||
               (text.Length >= 2 && (first == '-' || first == '*') && char.IsWhiteSpace(text[1])) ||
               StartsNumberedItem(text);
    }

    private static bool StartsNumberedItem(string text)
    {
        int index = 0;
        while (index < text.Length && char.IsDigit(text[index]))
        {
            index++;
        }

        return index > 0 &&
               index < text.Length - 1 &&
               (text[index] == '.' || text[index] == ')') &&
               char.IsWhiteSpace(text[index + 1]);
    }
}
