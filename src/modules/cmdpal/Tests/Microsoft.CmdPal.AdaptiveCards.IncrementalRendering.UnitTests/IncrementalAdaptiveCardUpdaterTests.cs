// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text.Json;

namespace Microsoft.CmdPal.AdaptiveCards.IncrementalRendering.UnitTests;

[TestClass]
public sealed class IncrementalAdaptiveCardUpdaterTests
{
    [TestMethod]
    public void JsonSnapshotFailureDisablesIncrementalUpdate()
    {
        var snapshot = IncrementalAdaptiveCardUpdater.TryCreateSnapshot(
            () => throw new JsonException("Test snapshot failure"));

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public void ComSnapshotFailureDisablesIncrementalUpdate()
    {
        var snapshot = IncrementalAdaptiveCardUpdater.TryCreateSnapshot(
            () => throw Marshal.GetExceptionForHR(unchecked((int)0x80004005))!);

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public void UnexpectedSnapshotFailureIsNotSuppressed()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            IncrementalAdaptiveCardUpdater.TryCreateSnapshot(
                () => throw new InvalidOperationException("Test failure")));
    }
}
