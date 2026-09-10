// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommonLibTest
{
    [TestClass]
    public class LaserPointerSettingsTests
    {
        private static readonly string[] ExpectedHotkeyHeaderKeys =
        {
            "MouseUtils_LaserPointer_ActivationShortcut",
            "MouseUtils_LaserPointer_PenActivationShortcut",
            "MouseUtils_LaserPointer_PresenterActivationShortcut",
            "MouseUtils_LaserPointer_PresenterStopShortcut",
        };

        [TestMethod]
        public void HotkeyAccessors_ShouldMatchRunnerHotkeyOrder()
        {
            var settings = new LaserPointerSettings();

            var accessors = settings.GetAllHotkeyAccessors();

            Assert.AreEqual(4, accessors.Length);
            CollectionAssert.AreEqual(
                ExpectedHotkeyHeaderKeys,
                accessors.Select(accessor => accessor.LocalizationHeaderKey).ToArray());
            Assert.AreSame(settings.Properties.ActivationShortcut, accessors[0].Value);
            Assert.AreSame(settings.Properties.PenActivationShortcut, accessors[1].Value);
            Assert.AreSame(settings.Properties.PresenterActivationShortcut, accessors[2].Value);
            Assert.AreSame(settings.Properties.PresenterStopShortcut, accessors[3].Value);
        }

        [TestMethod]
        public void DefaultHotkeys_ShouldSurviveSerializationRoundTrip()
        {
            var original = new LaserPointerSettings();

            AssertHotkey(original.Properties.ActivationShortcut, win: true, ctrl: false, alt: false, shift: true, code: 0x4C);
            AssertHotkey(original.Properties.PenActivationShortcut, win: false, ctrl: false, alt: false, shift: false, code: 0);
            AssertHotkey(original.Properties.PresenterActivationShortcut, win: true, ctrl: true, alt: false, shift: true, code: 0x57);
            AssertHotkey(original.Properties.PresenterStopShortcut, win: true, ctrl: true, alt: false, shift: true, code: 0x51);

            var deserialized = JsonSerializer.Deserialize<LaserPointerSettings>(original.ToJsonString());

            Assert.IsNotNull(deserialized);
            AssertHotkeyEqual(original.Properties.ActivationShortcut, deserialized.Properties.ActivationShortcut);
            AssertHotkeyEqual(original.Properties.PenActivationShortcut, deserialized.Properties.PenActivationShortcut);
            AssertHotkeyEqual(original.Properties.PresenterActivationShortcut, deserialized.Properties.PresenterActivationShortcut);
            AssertHotkeyEqual(original.Properties.PresenterStopShortcut, deserialized.Properties.PresenterStopShortcut);
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

        private static void AssertHotkeyEqual(HotkeySettings expected, HotkeySettings actual)
        {
            AssertHotkey(actual, expected.Win, expected.Ctrl, expected.Alt, expected.Shift, expected.Code);
        }
    }
}
