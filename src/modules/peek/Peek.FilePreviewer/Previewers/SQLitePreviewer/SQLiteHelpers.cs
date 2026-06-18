// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
    }
}
