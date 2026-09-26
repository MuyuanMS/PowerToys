// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CmdPal.Ext.WindowWalker.Components;

namespace Microsoft.CmdPal.Ext.WindowWalker.Pages;

/// <summary>
/// Offers to switch to an already open window of the app the user is searching for on the main page.
/// </summary>
internal sealed partial class FallbackWindowWalkerItem : FallbackCommandItem
{
    private const string _id = "com.microsoft.cmdpal.builtin.windowwalker.fallback";

    // UpdateQuery runs on every keystroke. Enumerating windows costs a round trip per window,
    // so reuse one snapshot while the user is typing.
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IconLoadDelay = TimeSpan.FromMilliseconds(50);
    private static readonly SemaphoreSlim IconLoadGate = new(1, 1);

    private readonly NoOpCommand _emptyCommand = new();
    private readonly Lock _updateLock = new();
    private readonly Lock _iconLoadLock = new();
    private List<WindowSearchEntry> _windows = [];
    private long _snapshotTimestamp;
    private long _queryGeneration;
    private WindowWalkerListItem? _currentItem;
    private CancellationTokenSource? _iconLoadCancellationTokenSource;

    public FallbackWindowWalkerItem()
        : base(Resources.windowwalker_fallback_title, _id)
    {
        Command = _emptyCommand;
        Title = string.Empty;
        Subtitle = string.Empty;
        Icon = Icons.WindowWalkerIcon;
    }

    public override void UpdateQuery(string query)
    {
        var queryGeneration = Interlocked.Increment(ref _queryGeneration);
        lock (_updateLock)
        {
            if (!IsCurrentQuery(queryGeneration))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < WindowMatcher.MinimumQueryLength)
            {
                Clear();
                return;
            }

            var windows = GetWindows();
            if (!IsCurrentQuery(queryGeneration))
            {
                return;
            }

            var index = WindowMatcher.FindBestMatch(windows, query, static w => w.Title, static w => w.ProcessName);
            if (!IsCurrentQuery(queryGeneration))
            {
                return;
            }

            if (index < 0)
            {
                Clear();
                return;
            }

            ShowWindow(windows[index].Window);
        }
    }

    private bool IsCurrentQuery(long queryGeneration) => queryGeneration == Volatile.Read(ref _queryGeneration);

    private List<WindowSearchEntry> GetWindows()
    {
        if (_snapshotTimestamp == 0 || Stopwatch.GetElapsedTime(_snapshotTimestamp) >= SnapshotLifetime)
        {
            WindowWalkerCommandsProvider.VirtualDesktopHelperInstance.UpdateDesktopList();
            OpenWindows.Instance.UpdateOpenWindowsList(CancellationToken.None);
            var windows = OpenWindows.Instance.Windows;
            var snapshot = new List<WindowSearchEntry>(windows.Count);
            foreach (var window in windows)
            {
                snapshot.Add(new WindowSearchEntry(window, window.Title, window.Process.Name));
            }

            _windows = snapshot;
            _snapshotTimestamp = Stopwatch.GetTimestamp();
        }

        return _windows;
    }

    private void ShowWindow(Window window)
    {
        var item = _currentItem;
        if (item?.Window?.Hwnd == window.Hwnd)
        {
            ResultHelper.UpdateResult(item, window);
        }
        else
        {
            item = ResultHelper.CreateResult(window);
            _currentItem = item;
        }

        if (item.NeedsIconLoad)
        {
            QueueIconLoad(item);
        }

        Command = item.Command;
        Title = item.Title;
        Subtitle = item.Subtitle;
        MoreCommands = item.MoreCommands;
    }

    private void QueueIconLoad(WindowWalkerListItem item)
    {
        Icon = Icons.GenericAppIcon;

        CancellationTokenSource cancellationTokenSource;
        CancellationTokenSource? previousCancellationTokenSource;
        lock (_iconLoadLock)
        {
            previousCancellationTokenSource = _iconLoadCancellationTokenSource;
            cancellationTokenSource = new CancellationTokenSource();
            _iconLoadCancellationTokenSource = cancellationTokenSource;
        }

        previousCancellationTokenSource?.Cancel();
        _ = Task.Run(() => LoadIconAsync(item, cancellationTokenSource));
    }

    private async Task LoadIconAsync(WindowWalkerListItem item, CancellationTokenSource cancellationTokenSource)
    {
        var cancellationToken = cancellationTokenSource.Token;
        try
        {
            await Task.Delay(IconLoadDelay, cancellationToken).ConfigureAwait(false);
            await IconLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_updateLock)
                {
                    if (!ReferenceEquals(_currentItem, item) || !item.NeedsIconLoad)
                    {
                        return;
                    }
                }

                item.LoadIcon();

                lock (_updateLock)
                {
                    if (ReferenceEquals(_currentItem, item))
                    {
                        Icon = item.Command?.Icon;
                    }
                }
            }
            finally
            {
                IconLoadGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_iconLoadLock)
            {
                if (ReferenceEquals(_iconLoadCancellationTokenSource, cancellationTokenSource))
                {
                    _iconLoadCancellationTokenSource = null;
                }
            }

            cancellationTokenSource.Dispose();
        }
    }

    private void Clear()
    {
        _currentItem = null;
        Command = _emptyCommand;
        Title = string.Empty;
        Subtitle = string.Empty;
        MoreCommands = [];
        Icon = Icons.WindowWalkerIcon;

        CancellationTokenSource? cancellationTokenSource;
        lock (_iconLoadLock)
        {
            cancellationTokenSource = _iconLoadCancellationTokenSource;
            _iconLoadCancellationTokenSource = null;
        }

        cancellationTokenSource?.Cancel();
    }

    private sealed record WindowSearchEntry(Window Window, string Title, string? ProcessName);
}
