// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Core.Selection;

public static class BrushPhraseBuilder
{
    private const string ClosingPunctuation = ".,!?;:%)]}>，。！？；：、";
    private const string OpeningPunctuation = "([{<（【「『";

    public static IReadOnlyList<RecognizedWord> GetSelectableWords(IReadOnlyList<TranslationLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<RecognizedWord> words = new();
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            TranslationLine line = lines[lineIndex];
            if (line.Words is { Count: > 0 })
            {
                words.AddRange(line.Words);
                continue;
            }

            string[] lineWords = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lineWords.Length == 0)
            {
                continue;
            }

            double wordWidth = line.BoundingBox.Width / lineWords.Length;
            for (int wordIndex = 0; wordIndex < lineWords.Length; wordIndex++)
            {
                words.Add(new RecognizedWord(
                    lineWords[wordIndex],
                    new PhysicalRect(
                        line.BoundingBox.X + (wordIndex * wordWidth),
                        line.BoundingBox.Y,
                        wordWidth,
                        line.BoundingBox.Height),
                    lineIndex,
                    wordIndex,
                    line.Confidence));
            }
        }

        return words;
    }

    public static BrushPhrase? Build(IReadOnlyCollection<RecognizedWord> selectedWords)
    {
        ArgumentNullException.ThrowIfNull(selectedWords);
        if (selectedWords.Count == 0)
        {
            return null;
        }

        List<RecognizedWord> orderedWords = selectedWords
            .OrderBy(word => word.LineIndex)
            .ThenBy(word => word.WordIndex)
            .ThenBy(word => word.BoundingBox.Top)
            .ThenBy(word => word.BoundingBox.Left)
            .ToList();

        StringBuilder phrase = new();
        PhysicalRect bounds = PhysicalRect.Empty;
        RecognizedWord? previousWord = null;
        foreach (RecognizedWord word in orderedWords)
        {
            string text = word.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (previousWord != null && ShouldInsertSpace(previousWord.Text, text))
            {
                phrase.Append(' ');
            }

            phrase.Append(text);
            bounds = bounds.Union(word.BoundingBox);
            previousWord = word;
        }

        return phrase.Length == 0 ? null : new BrushPhrase(phrase.ToString(), bounds, orderedWords);
    }

    private static bool ShouldInsertSpace(string previous, string current)
    {
        char previousCharacter = previous[^1];
        char currentCharacter = current[0];

        if (ClosingPunctuation.Contains(currentCharacter) ||
            OpeningPunctuation.Contains(previousCharacter) ||
            (IsCjk(previousCharacter) && IsCjk(currentCharacter)))
        {
            return false;
        }

        return true;
    }

    private static bool IsCjk(char character)
    {
        return (character >= '\u3040' && character <= '\u30ff') ||
               (character >= '\u3400' && character <= '\u9fff') ||
               (character >= '\uf900' && character <= '\ufaff');
    }
}
