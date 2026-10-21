// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CmdPal.Common;

namespace CoreWidgetProvider.Helpers;

internal sealed partial class DiskStats : PerformanceCounterSourceBase, IDisposable
{
    private readonly Dictionary<string, List<PerformanceCounter>> _diskCounters = new();
    private readonly List<string> _diskOrder = new();

    // Instances whose % Idle Time counter has a previous sample, so NextValue() returns a real value.
    private readonly HashSet<string> _idleTimeSampled = new();
    private bool _diskCounterReadFailureLogged;

    private Dictionary<string, Data> DiskUsages { get; set; } = new();

    // Keyed by counter instance name; _diskOrder defines the disk index in discovery order.
    private Dictionary<string, DiskHistory> DiskHistories { get; set; } = new();

    /// <summary>Active time (percent) and read and write rates (bytes per second), one sample per update.</summary>
    internal sealed record DiskHistory(SampleHistory ActiveTime, SampleHistory Read, SampleHistory Write);

    public sealed class Data
    {
        public float Usage
        {
            get; set;
        }

        public float Read
        {
            get; set;
        }

        public float Written
        {
            get; set;
        }

        /// <summary>Gets or sets the average time each transfer took, in seconds.</summary>
        public float? ResponseTime
        {
            get; set;
        }
    }

    public DiskStats()
    {
        InitDiskPerfCounters();
    }

    private void InitDiskPerfCounters()
    {
        try
        {
            var perfCounterCategory = CreatePerformanceCounterCategory("PhysicalDisk");
            if (perfCounterCategory is null)
            {
                return;
            }

            var instanceNames = perfCounterCategory.GetInstanceNames();
            foreach (var instanceName in instanceNames)
            {
                if (string.Equals(instanceName, "_Total", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var bytesRead = CreatePerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instanceName, logFailure: false);
                    var bytesWritten = CreatePerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instanceName, logFailure: false);

                    // Active time is 100 minus idle time, as in Task Manager. Unlike active time, % Disk Time
                    // can exceed 100% when requests overlap.
                    var idleTime = CreatePerformanceCounter("PhysicalDisk", "% Idle Time", instanceName, logFailure: false);
                    if (bytesRead is null || bytesWritten is null || idleTime is null)
                    {
                        bytesRead?.Dispose();
                        bytesWritten?.Dispose();
                        idleTime?.Dispose();
                        continue;
                    }

                    var instanceCounters = new List<PerformanceCounter> { bytesRead, bytesWritten, idleTime };

                    // Optional: Task Manager's average response time.
                    if (CreatePerformanceCounter("PhysicalDisk", "Avg. Disk sec/Transfer", instanceName, logFailure: false) is PerformanceCounter responseTime)
                    {
                        instanceCounters.Add(responseTime);
                    }

                    _diskCounters.Add(instanceName, instanceCounters);
                    DiskHistories.Add(instanceName, new DiskHistory(new SampleHistory(), new SampleHistory(), new SampleHistory()));
                    DiskUsages.Add(instanceName, new Data());
                    _diskOrder.Add(instanceName);
                }
                catch (Exception)
                {
                    // Skip interfaces whose counters cannot be initialized.
                }
            }
        }
        catch (Exception ex)
        {
            CoreLogger.LogError("Failed to initialize disk performance counters.", ex);
        }
    }

    public void GetData()
    {
        foreach (var diskCounterWithName in _diskCounters)
        {
            try
            {
                var read = diskCounterWithName.Value[0].NextValue();
                var written = diskCounterWithName.Value[1].NextValue();

                var name = diskCounterWithName.Key;
                var idlePercent = diskCounterWithName.Value[2].NextValue();
                var activeTimePercent = GetActiveTimePercent(idlePercent, hasPreviousSample: !_idleTimeSampled.Add(name));

                DiskUsages[name].Read = read;
                DiskUsages[name].Written = written;
                DiskUsages[name].Usage = activeTimePercent / 100f;
                if (diskCounterWithName.Value.Count > 3)
                {
                    DiskUsages[name].ResponseTime = GetResponseTime(diskCounterWithName.Value[3].NextValue());
                }

                var history = DiskHistories[name];
                history.ActiveTime.Add(activeTimePercent);
                history.Read.Add(read);
                history.Write.Add(written);
            }
            catch (Exception ex)
            {
                LogFailureOnce(ref _diskCounterReadFailureLogged, "Failed while reading disk performance counters.", ex);
            }
        }
    }

    /// <summary>
    /// Converts a % Idle Time reading to active time, the percentage of time the disk was busy, from 0 to 100.
    /// A counter's first reading has no previous sample and is 0, which would read as fully busy, so it counts
    /// as 0, as does a reading that isn't a number.
    /// </summary>
    internal static float GetActiveTimePercent(float idlePercent, bool hasPreviousSample) =>
        hasPreviousSample && float.IsFinite(idlePercent) ? Math.Clamp(100f - idlePercent, 0f, 100f) : 0f;

    internal static float? GetResponseTime(float seconds) =>
        float.IsFinite(seconds) ? Math.Max(0f, seconds) : null;

    public DiskHistory GetHistory(int diskIndex)
    {
        return (uint)diskIndex < (uint)_diskOrder.Count
            ? DiskHistories[_diskOrder[diskIndex]]
            : new DiskHistory(new SampleHistory(), new SampleHistory(), new SampleHistory());
    }

    public string GetDiskName(int diskIndex)
    {
        if ((uint)diskIndex >= (uint)_diskOrder.Count)
        {
            return string.Empty;
        }

        return _diskOrder[diskIndex];
    }

    public Data GetDiskUsage(int diskIndex)
    {
        if ((uint)diskIndex >= (uint)_diskOrder.Count)
        {
            return new Data();
        }

        var currDiskName = _diskOrder[diskIndex];
        if (!DiskUsages.TryGetValue(currDiskName, out var value))
        {
            return new Data();
        }

        return value;
    }

    public int GetPrevDiskIndex(int diskIndex)
    {
        if (_diskOrder.Count == 0)
        {
            return 0;
        }

        if (diskIndex == 0)
        {
            return _diskOrder.Count - 1;
        }

        return diskIndex - 1;
    }

    public int GetNextDiskIndex(int diskIndex)
    {
        if (_diskOrder.Count == 0)
        {
            return 0;
        }

        if (diskIndex == _diskOrder.Count - 1)
        {
            return 0;
        }

        return diskIndex + 1;
    }

    public void Dispose()
    {
        foreach (var counterPair in _diskCounters)
        {
            foreach (var counter in counterPair.Value)
            {
                counter.Dispose();
            }
        }
    }
}
