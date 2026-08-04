// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Peek.Common.Extensions;
using Peek.Common.Helpers;
using Peek.Common.Models;
using Peek.FilePreviewer.Models;
using Peek.FilePreviewer.Previewers.Interfaces;
using Peek.FilePreviewer.Previewers.SqlitePreviewer.Helpers;
using Peek.FilePreviewer.Previewers.SqlitePreviewer.Models;
using Windows.Foundation;

namespace Peek.FilePreviewer.Previewers.SqlitePreviewer
{
    public partial class SqlitePreviewer : ObservableObject, ISqlitePreviewer
    {
        [ObservableProperty]
        private PreviewState _state;

        [ObservableProperty]
        private string? _tableCountText;

        public ObservableCollection<SqliteTableInfo> Tables { get; } = [];

        private IFileSystemItem Item { get; }

        private DispatcherQueue Dispatcher { get; }

        private static readonly HashSet<string> _supportedFileTypes = [".db", ".sqlite", ".sqlite3"];

        public SqlitePreviewer(IFileSystemItem file)
        {
            Item = file;
            Dispatcher = DispatcherQueue.GetForCurrentThread();
        }

        public static bool IsItemSupported(IFileSystemItem item)
        {
            if (!_supportedFileTypes.Contains(item.Extension.ToLowerInvariant()))
            {
                return false;
            }

            try
            {
                using var stream = System.IO.File.OpenRead(item.Path);
                var buffer = new byte[16];
                int bytesRead = stream.Read(buffer, 0, 16);
                if (bytesRead == 16)
                {
                    var header = System.Text.Encoding.ASCII.GetString(buffer);
                    return header == "SQLite format 3\0";
                }
            }
            catch
            {
                // Ignored
            }

            return false;
        }

        public Task<PreviewSize> GetPreviewSizeAsync(CancellationToken cancellationToken)
        {
            var size = new Size(800, 500);
            return Task.FromResult(new PreviewSize { MonitorSize = size, UseEffectivePixels = true });
        }

        public async Task LoadPreviewAsync(CancellationToken cancellationToken)
        {
            State = PreviewState.Loading;

            var tables = await Task.Run(() => LoadTablesAsync(cancellationToken), cancellationToken);
            foreach (var table in tables)
            {
                await Dispatcher.RunOnUiThread(() => Tables.Add(table));
            }

            TableCountText = string.Format(
                CultureInfo.CurrentCulture,
                ResourceLoaderInstance.ResourceLoader.GetString("Sqlite_Table_Count"),
                tables.Count);

            State = PreviewState.Loaded;
        }

        private async Task<List<SqliteTableInfo>> LoadTablesAsync(CancellationToken cancellationToken)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Item.Path,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var tableNames = new List<string>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite\_%' ESCAPE '\' ORDER BY name;";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            var tables = new List<SqliteTableInfo>(tableNames.Count);
            foreach (var tableName in tableNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tableInfo = new SqliteTableInfo { Name = tableName };

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"PRAGMA table_xinfo({SqliteHelpers.QuoteIdentifier(tableName)});";
                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        if (reader.GetInt32(6) == 1)
                        {
                            continue;
                        }

                        tableInfo.Columns.Add(new SqliteColumnInfo
                        {
                            Name = reader.GetString(1),
                            Type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            IsNotNull = reader.GetInt32(3) == 1,
                            IsPrimaryKey = reader.GetInt32(5) > 0,
                        });
                    }
                }

                SqliteHelpers.AssignBindingKeys(tableInfo.Columns);
                tables.Add(tableInfo);
            }

            return tables;
        }

        public async Task LoadTableDataAsync(SqliteTableInfo tableInfo, CancellationToken cancellationToken)
        {
            if (tableInfo.IsLoaded)
            {
                return;
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Item.Path,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM {SqliteHelpers.QuoteIdentifier(tableInfo.Name)};";
                tableInfo.RowCount = (long)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
            }

            using (var cmd = connection.CreateCommand())
            {
                string projections = string.Join(
                    ", ",
                    tableInfo.Columns.Select(col =>
                    {
                        string identifier = SqliteHelpers.QuoteIdentifier(col.Name);
                        return $"{identifier}, typeof({identifier}), length({identifier})";
                    }));
                cmd.CommandText = $"SELECT {projections} FROM {SqliteHelpers.QuoteIdentifier(tableInfo.Name)} LIMIT 200;";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                var rows = new List<Dictionary<string, string?>>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, string?>(tableInfo.Columns.Count, StringComparer.Ordinal);
                    for (int columnIndex = 0; columnIndex < tableInfo.Columns.Count; columnIndex++)
                    {
                        var col = tableInfo.Columns[columnIndex];
                        int valueOrdinal = columnIndex * 3;
                        int typeOrdinal = valueOrdinal + 1;
                        int lengthOrdinal = valueOrdinal + 2;

                        if (reader.IsDBNull(valueOrdinal))
                        {
                            row[col.BindingKey] = null;
                        }
                        else if (reader.GetString(typeOrdinal) == "blob")
                        {
                            row[col.BindingKey] = string.Format(
                                CultureInfo.CurrentCulture,
                                ResourceLoaderInstance.ResourceLoader.GetString("Sqlite_Blob_Value"),
                                reader.GetInt64(lengthOrdinal));
                        }
                        else
                        {
                            row[col.BindingKey] = reader.GetValue(valueOrdinal)?.ToString();
                        }
                    }

                    rows.Add(row);
                }

                tableInfo.Rows = rows;
                tableInfo.IsLoaded = true;
            }
        }

        public async Task CopyAsync()
        {
            await Dispatcher.RunOnUiThread(async () =>
            {
                var storageItem = await Item.GetStorageItemAsync();
                ClipboardHelper.SaveToClipboard(storageItem);
            });
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
