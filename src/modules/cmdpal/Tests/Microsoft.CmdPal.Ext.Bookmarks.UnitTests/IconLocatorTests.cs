// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.CmdPal.Ext.Bookmarks.Helpers;
using Microsoft.CmdPal.Ext.Bookmarks.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.Bookmarks.UnitTests;

[TestClass]
public class IconLocatorTests
{
    [TestMethod]
    public async Task FileBookmarksUseShellItemIconProtocol()
    {
        var locator = new IconLocator();
        var path = @"C:\Files\bookmark.txt";

        var icon = await locator.GetIconForPath(new Classification(
            CommandKind.FileDocument,
            path,
            path,
            string.Empty,
            LaunchMethod.ShellExecute,
            WorkingDirectory: null,
            IsPlaceholder: false));

        var concrete = (IconInfo)icon;
        Assert.IsTrue(ShellItemIconProtocol.IsProtocol(concrete.Light.Icon));
        Assert.IsNull(concrete.Light.Data);
    }
}
