// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CoreWidgetProvider.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.PerformanceMonitor.UnitTests;

[TestClass]
public class DataTypeTests
{
    [TestMethod]
    public void NumericValuesRemainStableForExistingConsumers()
    {
        Assert.AreEqual(0, (int)DataType.CPU);
        Assert.AreEqual(1, (int)(DataType)1);
        Assert.AreEqual(2, (int)DataType.Memory);
        Assert.AreEqual(3, (int)DataType.GPU);
        Assert.AreEqual(4, (int)DataType.Network);
        Assert.AreEqual(5, (int)DataType.Battery);
        Assert.AreEqual(6, (int)DataType.Disk);
    }
}
