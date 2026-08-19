// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommonLibTest
{
    [TestClass]
    public class AdvancedPasteSettingsTests
    {
        [TestMethod]
        public void Deserialize_WithoutPasteAsSingleLineHotkey_ShouldUseUnassignedShortcut()
        {
            const string Json = "{\"name\":\"AdvancedPaste\",\"version\":\"1\",\"properties\":{}}";

            var deserialized = JsonSerializer.Deserialize<AdvancedPasteSettings>(Json);

            Assert.IsNotNull(deserialized);
            Assert.IsNotNull(deserialized.Properties.PasteAsSingleLineShortcut);
            AssertHotkey(deserialized.Properties.PasteAsSingleLineShortcut, win: false, ctrl: false, alt: false, shift: false, code: 0);
        }

        [TestMethod]
        public void RoundTrip_WithPasteAsSingleLineHotkey_ShouldPreserveShortcut()
        {
            var original = new AdvancedPasteSettings();
            original.Properties.PasteAsSingleLineShortcut = new HotkeySettings(win: true, ctrl: false, alt: true, shift: false, code: 0x53); // Win+Alt+S

            var json = original.ToJsonString();

            StringAssert.Contains(json, "paste-as-single-line-hotkey");

            var deserialized = JsonSerializer.Deserialize<AdvancedPasteSettings>(json);

            Assert.IsNotNull(deserialized);
            AssertHotkey(deserialized.Properties.PasteAsSingleLineShortcut, win: true, ctrl: false, alt: true, shift: false, code: 0x53);
        }

        private static void AssertHotkey(HotkeySettings hotkey, bool win, bool ctrl, bool alt, bool shift, int code)
        {
            Assert.IsNotNull(hotkey);
            Assert.AreEqual(win, hotkey.Win, "Win modifier mismatch.");
            Assert.AreEqual(ctrl, hotkey.Ctrl, "Ctrl modifier mismatch.");
            Assert.AreEqual(alt, hotkey.Alt, "Alt modifier mismatch.");
            Assert.AreEqual(shift, hotkey.Shift, "Shift modifier mismatch.");
            Assert.AreEqual(code, hotkey.Code, "Key code mismatch.");
        }
    }
}
