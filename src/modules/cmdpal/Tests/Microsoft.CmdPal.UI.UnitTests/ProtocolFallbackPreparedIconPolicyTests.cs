// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.UI.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public class ProtocolFallbackPreparedIconPolicyTests
{
    [TestMethod]
    public void RejectsMissingLocalUriFallback()
    {
        using var icon = IconPathConverter.PreparedIcon.FromUri(
            new Uri("file:///C:/Path/That/Should/Not/Exist/missing.png"),
            isSvg: false,
            targetSize: 20);

        Assert.IsFalse(ProtocolFallbackPreparedIconPolicy.ShouldUse(icon));
    }

    [TestMethod]
    public void AcceptsDecodableNonFileUriFallback()
    {
        using var icon = IconPathConverter.PreparedIcon.FromUri(
            new Uri("ms-appx:///Assets/fallback.png"),
            isSvg: false,
            targetSize: 20);

        Assert.IsTrue(ProtocolFallbackPreparedIconPolicy.ShouldUse(icon));
    }

    [TestMethod]
    public void RejectsNonDecodableUriFallback()
    {
        using var icon = IconPathConverter.PreparedIcon.FromUri(
            new Uri("steam://run/12345"),
            isSvg: false,
            targetSize: 20);

        Assert.IsFalse(ProtocolFallbackPreparedIconPolicy.ShouldUse(icon));
    }
}
