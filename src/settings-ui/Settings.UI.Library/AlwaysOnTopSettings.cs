// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Text.Json.Serialization;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class AlwaysOnTopSettings : BasePTModuleSettings, ISettingsConfig, IHotkeyConfig
    {
        public const string ModuleName = "AlwaysOnTop";
        public const string InitialModuleVersion = "0.0.1";
        public const string CurrentModuleVersion = "0.0.2";

        public AlwaysOnTopSettings()
        {
            Name = ModuleName;
            Version = CurrentModuleVersion;
            Properties = new AlwaysOnTopProperties();
        }

        [JsonPropertyName("properties")]
        public AlwaysOnTopProperties Properties { get; set; }

        public string GetModuleName()
        {
            return Name;
        }

        public ModuleType GetModuleType() => ModuleType.AlwaysOnTop;

        public HotkeyAccessor[] GetAllHotkeyAccessors()
        {
            var hotkeyAccessors = new List<HotkeyAccessor>
            {
                new HotkeyAccessor(
                    () => Properties.Hotkey.Value,
                    value => Properties.Hotkey.Value = value ?? AlwaysOnTopProperties.DefaultHotkeyValue,
                    "AlwaysOnTop_ActivationShortcut"),
                new HotkeyAccessor(
                    () => Properties.IncreaseOpacityHotkey.Value,
                    value => Properties.IncreaseOpacityHotkey.Value = value ?? AlwaysOnTopProperties.DefaultIncreaseOpacityHotkeyValue,
                    "AlwaysOnTop_IncreaseOpacityShortcut"),
                new HotkeyAccessor(
                    () => Properties.DecreaseOpacityHotkey.Value,
                    value => Properties.DecreaseOpacityHotkey.Value = value ?? AlwaysOnTopProperties.DefaultDecreaseOpacityHotkeyValue,
                    "AlwaysOnTop_DecreaseOpacityShortcut"),
            };

            return hotkeyAccessors.ToArray();
        }

        public bool UpgradeSettingsConfiguration()
        {
            if (string.IsNullOrWhiteSpace(Version) || string.Equals(Version, InitialModuleVersion, System.StringComparison.OrdinalIgnoreCase))
            {
                Properties.OpacitySoundEnabled = new BoolProperty(Properties.SoundEnabled.Value);
                Version = CurrentModuleVersion;
                return true;
            }

            return false;
        }
    }
}
