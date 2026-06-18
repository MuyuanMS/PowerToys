// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Peek.FilePreviewer.Previewers.SQLitePreviewer.Models;

namespace Peek.FilePreviewer.Previewers.SQLitePreviewer
{
    internal static class SQLiteHelpers
    {
        /// <summary>
        /// Quotes a SQLite identifier using double-quote syntax (SQL standard).
        /// Any internal double-quote characters are doubled to prevent injection.
        /// </summary>
        internal static string QuoteIdentifier(string identifier)
        {
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }

        /// <summary>
        /// Replaces characters that are special in WinUI property-path syntax
        /// with underscores so the indexer binding path works for any column name.
        /// </summary>
        internal static string SanitizeBindingKey(string name)
        {
            return name.Replace('.', '_').Replace('[', '_').Replace(']', '_').Replace('/', '_');
        }

        /// <summary>
        /// Assigns unique <see cref="SQLiteColumnInfo.BindingKey"/> values to each column,
        /// appending a numeric suffix when sanitization causes collisions.
        /// </summary>
        internal static void AssignBindingKeys(IList<SQLiteColumnInfo> columns)
        {
            var seen = new Dictionary<string, int>(columns.Count);
            foreach (var col in columns)
            {
                var key = SanitizeBindingKey(col.Name);
                if (seen.TryGetValue(key, out int count))
                {
                    seen[key] = count + 1;
                    key = $"{key}_{count + 1}";
                }
                else
                {
                    seen[key] = 1;
                }

                col.BindingKey = key;
            }
        }
    }
}
