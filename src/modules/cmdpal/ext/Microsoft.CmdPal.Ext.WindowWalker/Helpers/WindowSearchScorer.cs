// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.Ext.WindowWalker.Helpers;

/// <summary>
/// Scores an open window against a search query, matching the query against both the
/// window title and the owning process name.
/// </summary>
internal static class WindowSearchScorer
{
    /// <summary>
    /// Scores <paramref name="query"/> against a window's <paramref name="title"/> and
    /// <paramref name="processName"/>.
    /// </summary>
    /// <remarks>
    /// A single-word query is scored against each field as a whole, and the better of the two
    /// wins. A multi-word query is additionally scored word by word, with each word free to
    /// match either field, so that queries which name the app and part of its title together
    /// ("word budget") match in any order. Every word must match something for the word-by-word
    /// score to apply; otherwise the whole-query score stands. The result is never lower than
    /// the whole-query score, so anything that matched before still matches.
    /// </remarks>
    /// <param name="query">The user's search text.</param>
    /// <param name="title">The window title.</param>
    /// <param name="processName">The name of the process owning the window.</param>
    /// <returns>A score, where 0 means no match.</returns>
    internal static int Score(string? query, string? title, string? processName)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        title ??= string.Empty;
        processName ??= string.Empty;

        var wholeQueryScore = ScoreBothFields(query, title, processName);

        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2)
        {
            return wholeQueryScore;
        }

        var total = 0;
        foreach (var word in words)
        {
            var wordScore = ScoreBothFields(word, title, processName);
            if (wordScore == 0)
            {
                // A word that matches neither field means this window isn't what was asked for.
                return wholeQueryScore;
            }

            total += wordScore;
        }

        // Average rather than sum, to keep the per-word score on the same scale as the
        // whole-query score the two are compared against.
        return Math.Max(wholeQueryScore, total / words.Length);
    }

    /// <summary>
    /// Scores a collection of windows against the same query.
    /// </summary>
    /// <remarks>
    /// Each query component is scored across the full window collection before moving to the
    /// next component. This lets <see cref="FuzzyStringMatcher"/> reuse its prepared query
    /// while Window Walker filters a large window list.
    /// </remarks>
    internal static int[] ScoreAll<T>(
        string? query,
        IReadOnlyList<T> candidates,
        Func<T, string?> titleSelector,
        Func<T, string?> processNameSelector)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(titleSelector);
        ArgumentNullException.ThrowIfNull(processNameSelector);

        var scores = new int[candidates.Count];
        if (string.IsNullOrWhiteSpace(query))
        {
            return scores;
        }

        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ScoreAcrossCandidates(query, candidates, titleSelector, processNameSelector, scores);
        if (words.Length < 2)
        {
            return scores;
        }

        var totals = new int[candidates.Count];
        var allWordsMatched = new bool[candidates.Count];
        Array.Fill(allWordsMatched, true);

        foreach (var word in words)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var wordScore = ScoreBothFields(word, titleSelector(candidate) ?? string.Empty, processNameSelector(candidate) ?? string.Empty);
                if (wordScore == 0)
                {
                    allWordsMatched[index] = false;
                }
                else
                {
                    totals[index] += wordScore;
                }
            }
        }

        for (var index = 0; index < candidates.Count; index++)
        {
            if (allWordsMatched[index])
            {
                scores[index] = Math.Max(scores[index], totals[index] / words.Length);
            }
        }

        return scores;
    }

    private static void ScoreAcrossCandidates<T>(
        string query,
        IReadOnlyList<T> candidates,
        Func<T, string?> titleSelector,
        Func<T, string?> processNameSelector,
        int[] scores)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            scores[index] = ScoreBothFields(query, titleSelector(candidate) ?? string.Empty, processNameSelector(candidate) ?? string.Empty);
        }
    }

    private static int ScoreBothFields(string needle, string title, string processName)
        => Math.Max(
            FuzzyStringMatcher.ScoreFuzzy(needle, title),
            FuzzyStringMatcher.ScoreFuzzy(needle, processName));
}
