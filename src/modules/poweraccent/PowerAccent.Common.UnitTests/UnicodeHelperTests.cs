// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerAccent.Common;

namespace PowerAccent.Common.UnitTests;

[TestClass]
public sealed class UnicodeHelperTests
{
    [DataTestMethod]
    [DataRow("\u0301", "◌\u0301")]
    [DataRow("\u0301\u0300", "◌\u0301\u0300")]
    [DataRow("y\u0300", "y\u0300")]
    [DataRow("😀", "😀")]
    public void GetDisplayText_PreservesBaseCharactersAndMarks(string value, string expected)
    {
        Assert.AreEqual(expected, UnicodeHelper.GetDisplayText(value));
    }

    [TestMethod]
    public void GetDisplayText_EmptyValue_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, UnicodeHelper.GetDisplayText(string.Empty));
    }
}
