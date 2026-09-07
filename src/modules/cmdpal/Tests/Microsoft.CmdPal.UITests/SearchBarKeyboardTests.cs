// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.PowerToys.UITest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UITests;

[TestClass]
public class SearchBarKeyboardTests : CommandPaletteTestBase
{
    [TestMethod]
    public void ModifiedNavigationKeysPreserveSearchText()
    {
        SetSearchBox("windows");

        var searchBox = this.Find<TextBox>(By.AccessibilityId("MainSearchBox"));
        var initialText = searchBox.Text;
        var initialSelection = GetSelectedResult().Name;

        searchBox.Click();
        this.SendKeys(Key.Ctrl, Key.Up);
        this.SendKeys(Key.Shift, Key.Right);
        this.SendKeys(Key.Ctrl, Key.Left);
        this.SendKeys(Key.Ctrl, Key.PageDown);
        this.SendKeys(Key.Ctrl, Key.PageUp);
        this.SendKeys(Key.Shift, Key.Down);

        Assert.AreEqual(initialText, searchBox.Text);
        Assert.AreEqual(initialSelection, GetSelectedResult().Name);
    }

    [TestMethod]
    public void UnmodifiedNavigationKeysMoveTheSelectedResult()
    {
        SetSearchBox("windows");

        var initialSelection = GetSelectedResult().Name;

        this.SendKeys(Key.Down);
        Assert.AreNotEqual(initialSelection, GetSelectedResult().Name);

        this.SendKeys(Key.Up);
        Assert.AreEqual(initialSelection, GetSelectedResult().Name);
    }

    [TestMethod]
    public void UnmodifiedNavigationKeysMoveAnActiveGridParameterSelection()
    {
        SetSearchBox("Sample Pages");
        this.Find<NavigationViewItem>("Sample Pages").DoubleClick();

        this.Find<NavigationViewItem>("Create note sample (grid view)").DoubleClick();
        this.Find<Button>("Select folder").Click();

        var initialSelection = GetSelectedResult().Name;

        this.SendKeys(Key.Right);
        Assert.AreNotEqual(initialSelection, GetSelectedResult().Name);

        this.SendKeys(Key.Left);
        Assert.AreEqual(initialSelection, GetSelectedResult().Name);
    }

    private NavigationViewItem GetSelectedResult()
    {
        var selectedResult = this.FindAll<NavigationViewItem>(By.XPath("//*[@Name]"))
            .FirstOrDefault(item => item.Selected && !string.IsNullOrWhiteSpace(item.Name));

        Assert.IsNotNull(selectedResult, "No selected Command Palette result was found.");
        return selectedResult;
    }
}
