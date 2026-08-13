// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Enumerations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommonLibTest
{
    [TestClass]
    public class ClipPingSettingsTests
    {
        [TestMethod]
        public void DefaultsShouldMatchExpectedOverlay()
        {
            var settings = new ClipPingSettings();

            Assert.AreEqual(ClipPingSettings.ModuleName, settings.Name);
            Assert.AreEqual(ClipPingSettings.ModuleVersion, settings.Version);
            Assert.AreEqual("#FF0000", settings.Properties.OverlayColor.Value);
            Assert.AreEqual(ClipPingOverlay.Top, settings.Properties.OverlayType);
        }

        [TestMethod]
        public void RoundTripShouldPreserveValues()
        {
            var original = new ClipPingSettings();
            original.Properties.OverlayColor.Value = "#12ABEF";
            original.Properties.OverlayType = ClipPingOverlay.Border;

            var deserialized = JsonSerializer.Deserialize<ClipPingSettings>(
                original.ToJsonString(),
                SettingsSerializationContext.Default.ClipPingSettings);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual("#12ABEF", deserialized.Properties.OverlayColor.Value);
            Assert.AreEqual(ClipPingOverlay.Border, deserialized.Properties.OverlayType);
        }

        [TestMethod]
        public void ShouldBeRegisteredInSerializationContext()
        {
            var options = new JsonSerializerOptions
            {
                TypeInfoResolver = SettingsSerializationContext.Default,
            };

            var typeInfo = options.TypeInfoResolver.GetTypeInfo(typeof(ClipPingSettings), options);

            Assert.IsNotNull(typeInfo);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("#123")]
        [DataRow("#GG0000")]
        [DataRow("FF0000")]
        public void NormalizeOverlayColorShouldUseDefaultForInvalidValues(string value)
        {
            Assert.AreEqual(ClipPingProperties.DefaultOverlayColor, ClipPingProperties.NormalizeOverlayColor(value));
        }

        [TestMethod]
        public void NormalizeOverlayColorShouldPreserveValidValue()
        {
            Assert.AreEqual("#12abEF", ClipPingProperties.NormalizeOverlayColor("#12abEF"));
        }
    }
}
