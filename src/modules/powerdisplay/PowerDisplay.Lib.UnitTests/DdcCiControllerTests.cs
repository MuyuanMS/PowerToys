// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Common.Drivers.DDC;

namespace PowerDisplay.UnitTests;

[TestClass]
public class DdcCiControllerTests
{
    [TestMethod]
    public void TryScaleContrastToRawWriteValue_Max50AtOnePercent_ClampsRawToOne()
    {
        var ok = DdcCiController.TryScaleContrastToRawWriteValue(1, contrastVcpMax: 50, out var raw);

        Assert.IsTrue(ok);
        Assert.AreEqual(1, raw);
    }

    [TestMethod]
    public void TryScaleContrastToRawWriteValue_ZeroPercent_StillClampsRawToOne()
    {
        var ok = DdcCiController.TryScaleContrastToRawWriteValue(0, contrastVcpMax: 100, out var raw);

        Assert.IsTrue(ok);
        Assert.AreEqual(1, raw);
    }

    [TestMethod]
    public void TryScaleContrastToRawWriteValue_InvalidRange_ReturnsFalse()
    {
        var ok = DdcCiController.TryScaleContrastToRawWriteValue(50, contrastVcpMax: 0, out var raw);

        Assert.IsFalse(ok);
        Assert.AreEqual(0, raw);
    }
}
