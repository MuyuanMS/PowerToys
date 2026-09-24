// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;
using Microsoft.CmdPal.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Microsoft.CmdPal.UI.Helpers;

internal sealed class CachedIconSourceProvider : IIconSourceProvider
{
    private readonly AdaptiveCache<IconCacheKey, Task<IconSource?>> _cache;
    private readonly object _inFlightLock = new();
    private readonly Dictionary<IconCacheKey, InFlightLoad> _inFlight = [];
    private readonly Size _iconSize;
    private readonly int _cacheSize;
    private readonly IIconLoaderService _loader;

    public CachedIconSourceProvider(IIconLoaderService loader, Size iconSize, int cacheSize)
    {
        _loader = loader;
        _iconSize = iconSize;
        _cacheSize = cacheSize;
        _cache = new AdaptiveCache<IconCacheKey, Task<IconSource?>>(
            cacheSize,
            TimeSpan.FromMinutes(60),
            removalCallback: OnCacheEntryRemoved);
    }

    public CachedIconSourceProvider(IIconLoaderService loader, int iconSize, int cacheSize)
        : this(loader, new Size(iconSize, iconSize), cacheSize)
    {
    }

    public Task<IconSource?> GetIconSource(IconDataViewModel icon, double scale, IconRequestMeasurement diagnostics = default)
    {
        var key = new IconCacheKey(icon, scale);

        if (_cache.TryGet(key, out var existingTask))
        {
            IconLoadDiagnostics.RecordCacheLookup(_iconSize, _cacheSize, hit: true, _cache.ApproximateCount);
            diagnostics.RecordProviderResolution(IconProviderResolution.CacheHit, existingTask);
            return existingTask;
        }

        IconLoadDiagnostics.RecordCacheLookup(_iconSize, _cacheSize, hit: false, _cache.ApproximateCount);
        return GetOrCreateSlowPath(key, icon, scale, diagnostics);
    }

    private Task<IconSource?> GetOrCreateSlowPath(
        IconCacheKey key,
        IconDataViewModel icon,
        double scale,
        IconRequestMeasurement diagnostics)
    {
        var tcs = new TaskCompletionSource<IconSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = tcs.Task;
        var streamReference = icon.Data?.Unsafe;
        IconLoadMeasurement? loadDiagnostics;
        InFlightLoad? pending;

        lock (_inFlightLock)
        {
            if (_inFlight.TryGetValue(key, out pending))
            {
                diagnostics.RecordProviderResolution(IconProviderResolution.InFlight, pending.Diagnostics);
                return pending.Task;
            }

            try
            {
                loadDiagnostics = IconLoadDiagnostics.CreateLoad(
                    diagnostics,
                    icon.Icon,
                    streamReference is not null,
                    _iconSize.Width,
                    _iconSize.Height,
                    scale);
                loadDiagnostics?.RegisterTask(task);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                return task;
            }

            pending = new InFlightLoad(task, loadDiagnostics);
            _inFlight.Add(key, pending);
        }

        var newLoad = pending!;
        _ = task.ContinueWith(
            completed =>
            {
                try
                {
                    if (completed.IsCompletedSuccessfully)
                    {
                        _cache.Add(key, completed);
                        IconLoadDiagnostics.RecordCacheEntryAdded(
                            _iconSize,
                            _cacheSize,
                            _cache.ApproximateCount);
                    }
                }
                finally
                {
                    lock (_inFlightLock)
                    {
                        if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, newLoad))
                        {
                            _inFlight.Remove(key);
                        }
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        try
        {
            diagnostics.RecordProviderResolution(IconProviderResolution.NewLoad, loadDiagnostics);

            if (!_loader.TryEnqueueLoad(
                    icon.Icon,
                    icon.FontFamily,
                    streamReference,
                    _iconSize,
                    scale,
                    tcs,
                    IconLoadPriority.Low,
                    loadDiagnostics))
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(IIconLoaderService)));
            }
        }
        catch (Exception ex)
        {
            loadDiagnostics?.Rejected();
            tcs.TrySetException(ex);
        }

        return task;
    }

    private sealed record InFlightLoad(Task<IconSource?> Task, IconLoadMeasurement? Diagnostics);

    private void OnCacheEntryRemoved(
        IconCacheKey key,
        Task<IconSource?> task,
        AdaptiveCacheRemovalReason reason,
        int remainingCount,
        int capacity)
    {
        _ = key;
        _ = task;
        IconLoadDiagnostics.RecordCacheEntryRemoved(
            _iconSize,
            capacity,
            remainingCount,
            reason);
    }

    private readonly struct IconCacheKey : IEquatable<IconCacheKey>
    {
        private readonly string? _icon;
        private readonly string? _fontFamily;
        private readonly int _streamRefHashCode;
        private readonly int _scale;

        public IconCacheKey(IconDataViewModel icon, double scale)
        {
            _icon = icon.Icon;
            _fontFamily = icon.FontFamily;
            _streamRefHashCode = icon.Data?.Unsafe is { } stream
                ? RuntimeHelpers.GetHashCode(stream)
                : 0;
            _scale = (int)(100 * Math.Round(scale, 2));
        }

        public bool Equals(IconCacheKey other) =>
            _icon == other._icon &&
            _fontFamily == other._fontFamily &&
            _streamRefHashCode == other._streamRefHashCode &&
            _scale == other._scale;

        public override bool Equals(object? obj) => obj is IconCacheKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_icon, _fontFamily, _streamRefHashCode, _scale);
    }
}
