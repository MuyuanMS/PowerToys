// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommonLibTest
{
    [TestClass]
    public class FindMyMouseSettingsTest
    {
        /// <summary>
        /// Verifies that new (fresh install) settings have version "1.2" and
        /// ARGB defaults, so UpgradeSettingsConfiguration returns false.
        /// </summary>
        [TestMethod]
        public void NewSettingsShouldBeAtCurrentVersionAndRequireNoUpgrade()
        {
            var settings = new FindMyMouseSettings();

            Assert.AreEqual("1.2", settings.Version);
            bool upgraded = settings.UpgradeSettingsConfiguration();
            Assert.IsFalse(upgraded, "New settings should not require an upgrade.");
            Assert.AreEqual("1.2", settings.Version);
        }

        [TestMethod]
        public void GetSettingsShouldMigrateLegacyOverlayOpacityAndDropItFromSavedJson()
        {
            var mockFileSystem = new MockFileSystem();
            var settingsUtils = new SettingsUtils(mockFileSystem);
            string settingsPath = settingsUtils.GetSettingsFilePath(FindMyMouseSettings.ModuleName);
            const string legacySettingsJson = """
                {"name":"FindMyMouse","version":"1.1","properties":{"background_color":{"value":"#000000"},"spotlight_color":{"value":"#FFFFFF"},"overlay_opacity":{"value":50}}}
                """;

            mockFileSystem.AddFile(settingsPath, new MockFileData(legacySettingsJson));

            var settings = settingsUtils.GetSettings<FindMyMouseSettings>(FindMyMouseSettings.ModuleName);
            string savedJson = mockFileSystem.File.ReadAllText(settingsPath);
            using JsonDocument savedDocument = JsonDocument.Parse(savedJson);
            JsonElement properties = savedDocument.RootElement.GetProperty("properties");

            Assert.AreEqual("1.2", settings.Version);
            Assert.AreEqual("#80000000", settings.Properties.BackgroundColor.Value);
            Assert.AreEqual("#80FFFFFF", settings.Properties.SpotlightColor.Value);
            Assert.IsNull(settings.Properties.LegacyOverlayOpacity);

            Assert.AreEqual("#80000000", properties.GetProperty("background_color").GetProperty("value").GetString());
            Assert.AreEqual("#80FFFFFF", properties.GetProperty("spotlight_color").GetProperty("value").GetString());
            Assert.IsFalse(properties.TryGetProperty("overlay_opacity", out _));
        }

        /// <summary>
        /// Version "1.1" settings with old RGB colors and an overlay_opacity value
        /// should be migrated: colors converted to ARGB with the corresponding alpha.
        /// </summary>
        [TestMethod]
        public void UpgradeShouldConvertRgbColorsToArgbUsingOverlayOpacity()
        {
            var settings = new FindMyMouseSettings
            {
                Version = "1.1",
            };
            settings.Properties.BackgroundColor = new StringProperty("#000000");
            settings.Properties.SpotlightColor = new StringProperty("#FFFFFF");
            settings.Properties.LegacyOverlayOpacity = new IntProperty(50);

            bool upgraded = settings.UpgradeSettingsConfiguration();

            Assert.IsTrue(upgraded, "Settings at version 1.1 should require an upgrade.");
            Assert.AreEqual("1.2", settings.Version);

            // 50% opacity → alpha = (50 * 255 + 50) / 100 = 128 = 0x80
            Assert.AreEqual("#80000000", settings.Properties.BackgroundColor.Value);
            Assert.AreEqual("#80FFFFFF", settings.Properties.SpotlightColor.Value);
            Assert.IsNull(settings.Properties.LegacyOverlayOpacity, "LegacyOverlayOpacity should be cleared after migration.");
        }

        /// <summary>
        /// When overlay_opacity is absent (null) in version "1.1" settings,
        /// the migration should fall back to 50% opacity (alpha = 0x80).
        /// </summary>
        [TestMethod]
        public void UpgradeShouldDefaultTo50PercentOpacityWhenOverlayOpacityIsMissing()
        {
            var settings = new FindMyMouseSettings
            {
                Version = "1.1",
            };
            settings.Properties.BackgroundColor = new StringProperty("#000000");
            settings.Properties.SpotlightColor = new StringProperty("#FFFFFF");
            settings.Properties.LegacyOverlayOpacity = null;

            bool upgraded = settings.UpgradeSettingsConfiguration();

            Assert.IsTrue(upgraded);
            Assert.AreEqual("1.2", settings.Version);
            Assert.AreEqual("#80000000", settings.Properties.BackgroundColor.Value);
            Assert.AreEqual("#80FFFFFF", settings.Properties.SpotlightColor.Value);
        }

        /// <summary>
        /// Invalid legacy overlay_opacity values should fall back to 50% opacity
        /// (alpha = 0x80) during migration.
        /// </summary>
        [DataTestMethod]
        [DataRow(-1)]
        [DataRow(101)]
        public void UpgradeShouldDefaultTo50PercentOpacityWhenOverlayOpacityIsOutOfRange(int legacyOverlayOpacity)
        {
            var settings = new FindMyMouseSettings
            {
                Version = "1.1",
            };
            settings.Properties.BackgroundColor = new StringProperty("#000000");
            settings.Properties.SpotlightColor = new StringProperty("#FFFFFF");
            settings.Properties.LegacyOverlayOpacity = new IntProperty(legacyOverlayOpacity);

            bool upgraded = settings.UpgradeSettingsConfiguration();

            Assert.IsTrue(upgraded);
            Assert.AreEqual("1.2", settings.Version);
            Assert.AreEqual("#80000000", settings.Properties.BackgroundColor.Value);
            Assert.AreEqual("#80FFFFFF", settings.Properties.SpotlightColor.Value);
            Assert.IsNull(settings.Properties.LegacyOverlayOpacity, "LegacyOverlayOpacity should be cleared after migration.");
        }

        /// <summary>
        /// Version "1.1" settings that already have ARGB colors (9-char #AARRGGBB)
        /// should not have their alpha modified during migration.
        /// </summary>
        [TestMethod]
        public void UpgradeShouldNotModifyAlreadyArgbColors()
        {
            var settings = new FindMyMouseSettings
            {
                Version = "1.1",
            };
            settings.Properties.BackgroundColor = new StringProperty("#80000000");
            settings.Properties.SpotlightColor = new StringProperty("#80FFFFFF");
            settings.Properties.LegacyOverlayOpacity = new IntProperty(100); // would produce 0xFF if applied

            bool upgraded = settings.UpgradeSettingsConfiguration();

            Assert.IsTrue(upgraded);
            Assert.AreEqual("1.2", settings.Version);

            // Colors should be unchanged because they were already in #AARRGGBB format
            Assert.AreEqual("#80000000", settings.Properties.BackgroundColor.Value);
            Assert.AreEqual("#80FFFFFF", settings.Properties.SpotlightColor.Value);
        }

        /// <summary>
        /// Very old version "1.0" settings should be migrated to "1.2" in one call
        /// (the activation method fix and the color migration both run).
        /// </summary>
        [TestMethod]
        public void UpgradeShouldHandleVersion10ToVersion12()
        {
            var settings = new FindMyMouseSettings
            {
                Version = "1.0",
            };
            settings.Properties.ActivationMethod = new IntProperty(1); // legacy value that should become 2
            settings.Properties.BackgroundColor = new StringProperty("#000000");
            settings.Properties.SpotlightColor = new StringProperty("#FFFFFF");
            settings.Properties.LegacyOverlayOpacity = new IntProperty(75);

            bool upgraded = settings.UpgradeSettingsConfiguration();

            Assert.IsTrue(upgraded);
            Assert.AreEqual("1.2", settings.Version);
            Assert.AreEqual(2, settings.Properties.ActivationMethod.Value, "Activation method should have been fixed from 1 to 2.");

            // 75% opacity → alpha = (75 * 255 + 50) / 100 = 191 = 0xBF
            Assert.AreEqual("#BF000000", settings.Properties.BackgroundColor.Value);
            Assert.AreEqual("#BFFFFFFF", settings.Properties.SpotlightColor.Value);
        }
    }
}
