// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.UI.ViewModels;

/// <summary>
/// One requester's interest in initializing an item. Its lifetime is independent
/// of the coordinator, and releasing it cannot cancel another requester's demand.
/// </summary>
internal sealed class ListItemInitializationDemand(
    ListItemViewModel item,
    CancellationToken cancellationToken,
    bool pruneOnRelease = true)
{
    private int _referenceCount = 1;

    internal ListItemViewModel Item { get; } = item;

    internal bool IsActive => Volatile.Read(ref _referenceCount) > 0 && !cancellationToken.IsCancellationRequested;

    internal bool TryAddReference()
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var referenceCount = Volatile.Read(ref _referenceCount);
            if (referenceCount <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _referenceCount, referenceCount + 1, referenceCount) == referenceCount)
            {
                return true;
            }
        }

        return false;
    }

    // Selection cancellation is observed when the worker examines the request.
    // No CTS callback executes coordinator work on the thread changing selection.
    internal void Release()
    {
        if (Interlocked.Decrement(ref _referenceCount) == 0)
        {
            if (pruneOnRelease)
            {
                Item.PruneInitializationDemand(this);
            }
        }
    }
}
