// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.Ext.Indexer.Data;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.Indexer.UnitTests;

[TestClass]
public class IndexerListItemTests
{
    [TestMethod]
    public void EmptyPathDoesNotCreateShellIconRequest()
    {
        var item = new IndexerListItem(
            new IndexerItem
            {
                FileName = "Result without a launch target",
                FullPath = string.Empty,
            });

        Assert.IsNull(item.Icon);
    }

    [TestMethod]
    public void NonEmptyPathCreatesShellIconRequest()
    {
        const string Path = @"C:\Files\bookmark.txt";
        var item = new IndexerListItem(
            new IndexerItem
            {
                FileName = "bookmark.txt",
                FullPath = Path,
            });

        Assert.IsNotNull(item.Icon);
        Assert.IsTrue(ShellItemIconProtocol.TryParse(item.Icon.Light.Icon, out var itemPath, out var jumbo));
        Assert.AreEqual(Path, itemPath);
        Assert.IsFalse(jumbo);
    }
}
