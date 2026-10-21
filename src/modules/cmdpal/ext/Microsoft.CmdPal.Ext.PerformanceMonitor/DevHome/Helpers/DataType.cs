// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace CoreWidgetProvider.Helpers;

public enum DataType
{
    /// <summary>
    /// CPU related data. The CPU card adds per-processor utilization and the busiest processes
    /// with <see cref="CPUStats.RequestDetails"/>.
    /// </summary>
    CPU = 0,

    /// <summary>
    /// Retained to preserve the numeric values of the following members for existing consumers.
    /// </summary>
    [System.Obsolete("Use CPU instead.")]
    CpuWithTopProcesses = 1,

    /// <summary>
    /// Memory related data.
    /// </summary>
    Memory = 2,

    /// <summary>
    /// GPU related data.
    /// </summary>
    GPU = 3,

    /// <summary>
    /// Network related data.
    /// </summary>
    Network = 4,

    /// <summary>
    /// Battery related data.
    /// </summary>
    Battery = 5,

    /// <summary>
    /// Disk related data.
    /// </summary>
    Disk = 6,
}
