// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class ScreenTranslatorSettings : BasePTModuleSettings, ISettingsConfig, IHotkeyConfig
    {
        public const string ModuleName = "ScreenTranslator";

        private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        [JsonPropertyName("properties")]
        public ScreenTranslatorProperties Properties { get; set; }

        public ScreenTranslatorSettings()
        {
            Properties = new ScreenTranslatorProperties();
            Version = "1";
            Name = ModuleName;
        }

        public virtual void Save(SettingsUtils settingsUtils)
        {
            var options = _serializerOptions;
            ArgumentNullException.ThrowIfNull(settingsUtils);
            settingsUtils.SaveSettings(JsonSerializer.Serialize(this, options), ModuleName);
        }

        public string GetModuleName() => Name;

        public ModuleType GetModuleType() => ModuleType.ScreenTranslator;

        public HotkeyAccessor[] GetAllHotkeyAccessors()
        {
            var hotkeyAccessors = new List<HotkeyAccessor>
            {
                new HotkeyAccessor(
                    () => Properties.ActivationShortcut,
                    value => Properties.ActivationShortcut = value ?? Properties.DefaultActivationShortcut,
                    "Activation_Shortcut"),
                new HotkeyAccessor(
                    () => Properties.CurrentScreenShortcut,
                    value => Properties.CurrentScreenShortcut = value ?? Properties.DefaultCurrentScreenShortcut,
                    "CurrentScreen_Shortcut"),
                new HotkeyAccessor(
                    () => Properties.ActiveWindowShortcut,
                    value => Properties.ActiveWindowShortcut = value ?? Properties.DefaultActiveWindowShortcut,
                    "ActiveWindow_Shortcut"),
                new HotkeyAccessor(
                    () => Properties.ScanTextShortcut,
                    value => Properties.ScanTextShortcut = value ?? Properties.DefaultScanTextShortcut,
                    "ScanText_Shortcut"),
            };

            return hotkeyAccessors.ToArray();
        }

        public bool UpgradeSettingsConfiguration()
        {
            HotkeySettings shortcut = Properties.ScanTextShortcut;
            if (shortcut is not null &&
                shortcut.Win &&
                shortcut.Ctrl &&
                shortcut.Shift &&
                !shortcut.Alt &&
                shortcut.Code == 0x42)
            {
                Properties.ScanTextShortcut = Properties.DefaultScanTextShortcut;
                return true;
            }

            return false;
        }
    }
}
