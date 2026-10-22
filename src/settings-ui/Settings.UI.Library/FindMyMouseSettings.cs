// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class FindMyMouseSettings : BasePTModuleSettings, ISettingsConfig, IHotkeyConfig
    {
        public const string ModuleName = "FindMyMouse";

        [JsonPropertyName("properties")]
        public FindMyMouseProperties Properties { get; set; }

        public FindMyMouseSettings()
        {
            Name = ModuleName;
            Properties = new FindMyMouseProperties();
            Version = "1.2";
        }

        public string GetModuleName()
        {
            return Name;
        }

        public ModuleType GetModuleType() => ModuleType.FindMyMouse;

        public HotkeyAccessor[] GetAllHotkeyAccessors()
        {
            var hotkeyAccessors = new List<HotkeyAccessor>
            {
                new HotkeyAccessor(
                    () => Properties.ActivationShortcut,
                    value => Properties.ActivationShortcut = value ?? Properties.DefaultActivationShortcut,
                    "MouseUtils_FindMyMouse_ActivationShortcut"),
            };

            return hotkeyAccessors.ToArray();
        }

        // This can be utilized in the future if the settings.json file is to be modified/deleted.
        public bool UpgradeSettingsConfiguration()
        {
            bool upgraded = false;

            if (Version == "1.0")
            {
                if (Properties.ActivationMethod.Value == 1)
                {
                    Properties.ActivationMethod = new IntProperty(2);
                }

                Version = "1.1";
                upgraded = true;
            }

            if (Version == "1.1")
            {
                // Migrate old RGB colors + legacy overlay_opacity to new ARGB format.
                // Old schema: colors stored as #RRGGBB + separate overlay_opacity (0-100 integer).
                // New schema: colors stored as #AARRGGBB with alpha channel embedded.
                int opacityPercent = Properties.LegacyOverlayOpacity?.Value ?? 50;
                if (opacityPercent < 0 || opacityPercent > 100)
                {
                    Logger.LogWarning($"FindMyMouse: invalid legacy overlay_opacity value {opacityPercent} encountered during migration; defaulting to 50.");
                    opacityPercent = 50;
                }

                // Round to nearest integer: (percent * 255 + 50) / 100
                byte alpha = (byte)(((opacityPercent * 255) + 50) / 100);

                string bgColor = Properties.BackgroundColor?.Value ?? string.Empty;
                if (bgColor.Length == 7 && bgColor.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                {
                    // Old RGB format (#RRGGBB) — prepend alpha to produce #AARRGGBB.
                    // ToUpperInvariant normalises the RGB portion in case the stored value used lowercase hex.
                    Properties.BackgroundColor = new StringProperty($"#{alpha:X2}{bgColor.Substring(1).ToUpperInvariant()}");
                }

                string spotlightColor = Properties.SpotlightColor?.Value ?? string.Empty;
                if (spotlightColor.Length == 7 && spotlightColor.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                {
                    Properties.SpotlightColor = new StringProperty($"#{alpha:X2}{spotlightColor.Substring(1).ToUpperInvariant()}");
                }

                // Clear legacy property so it is excluded from the re-saved settings file
                Properties.LegacyOverlayOpacity = null;

                Version = "1.2";
                upgraded = true;
            }

            return upgraded;
        }
    }
}
