// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Common.Search.FuzzSearch;
using ShortcutGuide.Models;

namespace ShortcutGuide.Helpers
{
    public static class ShortcutSearchMatcher
    {
        private const int FuzzyMatchThreshold = (int)SearchPrecisionScore.Regular;

        public static bool Matches(ShortcutEntry shortcut, string? query)
        {
            string searchText = query?.Trim() ?? string.Empty;
            if (searchText.Length == 0)
            {
                return true;
            }

            if (MatchesText(shortcut.Name, searchText) || MatchesText(shortcut.Description, searchText))
            {
                return true;
            }

            foreach (var description in shortcut.Shortcut ?? [])
            {
                if (MatchesText(GetChordSearchLabel(description), searchText))
                {
                    return true;
                }

                foreach (string label in GetSearchLabels(description))
                {
                    if (MatchesText(label, searchText))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string GetChordSearchLabel(ShortcutDescription description)
        {
            var labels = new List<string>();
            if (description.Win)
            {
                labels.Add("Win Windows");
            }

            if (description.Ctrl)
            {
                labels.Add("Ctrl Control");
            }

            if (description.Alt)
            {
                labels.Add("Alt");
            }

            if (description.Shift)
            {
                labels.Add("Shift");
            }

            labels.AddRange((description.Keys ?? []).Select(GetKeySearchLabel));
            return string.Join(' ', labels);
        }

        private static IEnumerable<string> GetSearchLabels(ShortcutDescription description)
        {
            if (description.Win)
            {
                yield return "Win Windows";
            }

            if (description.Ctrl)
            {
                yield return "Ctrl Control";
            }

            if (description.Alt)
            {
                yield return "Alt";
            }

            if (description.Shift)
            {
                yield return "Shift";
            }

            foreach (string key in description.Keys ?? [])
            {
                yield return GetKeySearchLabel(key);
            }
        }

        private static string GetKeySearchLabel(string key)
        {
            if (int.TryParse(key, out int keyCode))
            {
                return keyCode switch
                {
                    37 => "Left Left Arrow",
                    38 => "Up Up Arrow",
                    39 => "Right Right Arrow",
                    40 => "Down Down Arrow",
                    _ => Microsoft.PowerToys.Settings.UI.Library.Utilities.Helper.GetKeyName((uint)keyCode),
                };
            }

            return key switch
            {
                "Up" or "<Up>" => "Up Up Arrow",
                "Down" or "<Down>" => "Down Down Arrow",
                "Left" or "<Left>" => "Left Left Arrow",
                "Right" or "<Right>" => "Right Right Arrow",
                "Back" or "<Backspace>" => "Back Backspace",
                "<TASKBAR1-9>" => "Num",
                "<ArrowUD>" => "Up Down Arrow",
                "<ArrowLR>" => "Left Right Arrow",
                "<Arrow>" => "Left Right Up Down Arrow",
                "<Enter>" => "Enter",
                "<LessThan>" => "<",
                "<GreaterThan>" => ">",
                "<Escape>" => "Esc Escape",
                string value when value.StartsWith('<') && value.EndsWith('>') => value.Trim('<', '>'),
                _ => key,
            };
        }

        private static bool MatchesText(string? value, string searchText)
        {
            return value is not null &&
                   (value.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    StringMatcher.FuzzyMatch(searchText, value).RawScore >= FuzzyMatchThreshold);
        }
    }
}
