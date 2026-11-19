// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using ManagedCommon;
using Microsoft.CmdPal.Ext.Indexer.Data;
using Microsoft.CmdPal.Ext.Indexer.Indexer;
using Microsoft.CommandPalette.Extensions;

namespace Microsoft.CmdPal.Ext.Indexer;

public sealed partial class SearchEngine : IIndexerFallbackSearchEngine
{
    private SearchQuery? _searchQuery = new();

    public SearchNoticeInfo? Query(string query, uint queryCookie)
    {
        var searchQuery = StartQuery(query, queryCookie);
        return searchQuery is null ? null : BuildNotice(searchQuery);
    }

    void IIndexerFallbackSearchEngine.Query(string query, uint queryCookie)
    {
        StartQuery(query, queryCookie);
    }

    private SearchQuery? StartQuery(string query, uint queryCookie)
    {
        var searchQuery = _searchQuery;
        if (searchQuery is null)
        {
            return null;
        }

        searchQuery.SearchResults.Clear();
        searchQuery.CancelOutstandingQueries();

        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        Stopwatch stopwatch = new();
        stopwatch.Start();

        searchQuery.Execute(query, queryCookie);

        stopwatch.Stop();
        Logger.LogDebug($"Query time: {stopwatch.ElapsedMilliseconds} ms, query: \"{query}\"");

        return searchQuery;
    }

    public IList<IListItem> FetchItems(int offset, int limit, uint queryCookie, out bool hasMore, out SearchNoticeInfo? notice)
    {
        hasMore = false;
        notice = null;

        var searchQuery = _searchQuery;
        if (searchQuery is null)
        {
            return [];
        }

        var cookie = searchQuery.Cookie;
        if (cookie != queryCookie)
        {
            return [];
        }

        var results = new List<IListItem>();
        var index = 0;
        var hasMoreItems = searchQuery.FetchRows(offset, limit);
        notice = BuildNotice(searchQuery);

        while (!searchQuery.SearchResults.IsEmpty && searchQuery.SearchResults.TryDequeue(out var result) && ++index <= limit)
        {
            var indexerListItem = new IndexerListItem(new IndexerItem
            {
                FileName = result.ItemDisplayName,
                FullPath = result.LaunchUri,
            });

            results.Add(indexerListItem);
        }

        hasMore = hasMoreItems;
        return results;
    }

    public IList<IListItem> FetchNextItems(int limit, uint queryCookie, out bool hasMore, out SearchNoticeInfo? notice)
    {
        // The rowset keeps its cursor between calls; zero skips no rows from that position.
        return FetchItems(0, limit, queryCookie, out hasMore, out notice);
    }

    private static SearchNoticeInfo? BuildNotice(SearchQuery searchQuery)
    {
        return SearchNoticeInfoBuilder.FromQueryStatus(searchQuery.GetExecutionStatus())
            ?? SearchNoticeInfoBuilder.FromCatalogStatus(SearchCatalogStatusReader.GetStatus());
    }

    public void Dispose()
    {
        var searchQuery = _searchQuery;
        _searchQuery = null;

        searchQuery?.Dispose();

        GC.SuppressFinalize(this);
    }
}
