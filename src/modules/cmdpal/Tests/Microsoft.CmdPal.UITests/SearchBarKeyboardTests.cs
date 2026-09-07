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
        AssertModifiedNavigationKeyDoesNotChangeSelection(Key.Ctrl, Key.Up);
        AssertModifiedNavigationKeyDoesNotChangeSelection(Key.Shift, Key.Right);
        AssertModifiedNavigationKeyDoesNotChangeSelection(Key.Ctrl, Key.Left);
        AssertModifiedNavigationKeyDoesNotChangeSelection(Key.Ctrl, Key.PageDown);
        AssertModifiedNavigationKeyDoesNotChangeSelection(Key.Ctrl, Key.PageUp);
        AssertModifiedNavigationKeyDoesNotChangeSelection(Key.Shift, Key.Down);
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

        this.SendKeys(Key.Ctrl, Key.Right);
        Assert.AreEqual(initialSelection, GetSelectedResult().Name);

        this.SendKeys(Key.Shift, Key.Left);
        Assert.AreEqual(initialSelection, GetSelectedResult().Name);

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

    private void AssertModifiedNavigationKeyDoesNotChangeSelection(Key modifier, Key key)
    {
        SetSearchBox("windows");

        var searchBox = this.Find<TextBox>(By.AccessibilityId("MainSearchBox"));
        var initialText = searchBox.Text;
        var initialSelection = GetSelectedResult().Name;

        searchBox.Click();
        this.SendKeys(modifier, key);

        Assert.AreEqual(initialText, searchBox.Text);
        Assert.AreEqual(initialSelection, GetSelectedResult().Name);
    }
}
