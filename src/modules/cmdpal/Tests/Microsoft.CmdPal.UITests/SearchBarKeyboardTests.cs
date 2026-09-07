// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.PowerToys.UITest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UITests;

[TestClass]
public class SearchBarKeyboardTests : CommandPaletteTestBase
{
    [TestMethod]
    public void ModifiedNavigationKeysPreserveSearchText()
    {
        SetSearchBox("calculator");

        var searchBox = this.Find<TextBox>(By.AccessibilityId("MainSearchBox"));
        var initialText = searchBox.Text;

        searchBox.Click();
        this.SendKeys(Key.Ctrl, Key.Right);
        this.SendKeys(Key.Shift, Key.Right);
        this.SendKeys(Key.Ctrl, Key.PageDown);
        this.SendKeys(Key.Ctrl, Key.PageUp);

        Assert.AreEqual(initialText, searchBox.Text);
        Assert.IsNotNull(this.Find<NavigationViewItem>("Calculator"));
    }
}
