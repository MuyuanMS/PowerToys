// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;

namespace Microsoft.CmdPal.Ext.WindowWalker.Helpers;

/// <summary>
/// Scores an open window against a search query, matching the query against both the
/// window title and the owning process name.
/// </summary>
internal static class WindowSearchScorer
{
    internal sealed class ScoringState
    {
        private readonly FuzzyStringMatcher.PreparedQuery _wholeQuery;
        private readonly FuzzyStringMatcher.PreparedQuery? _normalizedQuery;
        private readonly FuzzyStringMatcher.PreparedQuery[] _words;

        private ScoringState(string query)
        {
            var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            _wholeQuery = FuzzyStringMatcher.Prepare(query);
            var normalizedQuery = string.Join(' ', words);
            _normalizedQuery = string.Equals(normalizedQuery, query, StringComparison.Ordinal)
                ? null
                : FuzzyStringMatcher.Prepare(normalizedQuery);
            _words = words.Length < 2
                ? []
                : words.Select(FuzzyStringMatcher.Prepare).ToArray();
        }

        internal static ScoringState Create(string query) => new(query);

        internal int Score(string title, string processName)
        {
            var wholeQueryScore = ScoreBothFields(_wholeQuery, title, processName);
            if (_normalizedQuery is not null)
            {
                wholeQueryScore = Math.Max(wholeQueryScore, ScoreBothFields(_normalizedQuery.Value, title, processName));
            }

            if (_words.Length < 2)
            {
                return wholeQueryScore;
            }

            var total = 0;
            foreach (var word in _words)
            {
                var wordScore = ScoreBothFields(word, title, processName);
                if (wordScore == 0)
                {
                    return wholeQueryScore;
                }

                total += wordScore;
            }

            return Math.Max(wholeQueryScore, total / _words.Length);
        }
    }

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
    /// the whole-query score, so anything that matched before still matches. Words are separated
    /// by any Unicode whitespace, and the query is scored both as typed and whitespace-normalized.
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

        return ScoringState.Create(query).Score(title, processName);
    }

    private static int ScoreBothFields(FuzzyStringMatcher.PreparedQuery needle, string title, string processName)
        => Math.Max(
            FuzzyStringMatcher.ScoreFuzzy(in needle, title),
            FuzzyStringMatcher.ScoreFuzzy(in needle, processName));

    private static int ScoreBothFields(string needle, string title, string processName)
        => Math.Max(
            FuzzyStringMatcher.ScoreFuzzy(needle, title),
            FuzzyStringMatcher.ScoreFuzzy(needle, processName));
}
