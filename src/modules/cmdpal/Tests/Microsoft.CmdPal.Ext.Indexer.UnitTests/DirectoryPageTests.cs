// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.Indexer.UnitTests;

[TestClass]
public class DirectoryPageTests
{
    [TestMethod]
    public void EmptyPathKeepsFileExplorerFallbackIcon()
    {
        var page = new DirectoryPage(string.Empty);

        Assert.AreSame(Icons.FileExplorerIcon, page.Icon);
    }

    [TestMethod]
    public void NonEmptyPathCreatesShellIconRequest()
    {
        const string Path = @"C:\Files";
        var page = new DirectoryPage(Path);

        Assert.IsNotNull(page.Icon);
        Assert.IsTrue(ShellItemIconProtocol.TryParse(page.Icon.Light.Icon, out var itemPath, out var jumbo));
        Assert.AreEqual(Path, itemPath);
        Assert.IsFalse(jumbo);
    }
}
