// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

using Microsoft.Windows.Globalization;
using Windows.Globalization.DateTimeFormatting;

namespace Microsoft.PowerToys.Settings.UI.OOBE.Views
{
    internal static class ScoobeDateFormatter
    {
        internal static DateTimeFormatter CreateMonthYearFormatter()
        {
            return Create("month year");
        }

        internal static DateTimeFormatter CreateReleaseDetailsDateFormatter()
        {
            return Create("month day year");
        }

        private static DateTimeFormatter Create(string template)
        {
            var languages = GetLanguages(ApplicationLanguages.PrimaryLanguageOverride, ApplicationLanguages.Languages);
            return new DateTimeFormatter(template, languages);
        }

        internal static IReadOnlyList<string> GetLanguages(string primaryLanguageOverride, IEnumerable<string> applicationLanguages)
        {
            var languages = new List<string>();
            var seenLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(primaryLanguageOverride) && seenLanguages.Add(primaryLanguageOverride))
            {
                languages.Add(primaryLanguageOverride);
            }

            foreach (var language in applicationLanguages)
            {
                if (seenLanguages.Add(language))
                {
                    languages.Add(language);
                }
            }

            return languages;
        }
    }
}
