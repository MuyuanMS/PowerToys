// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Settings.UI.Library.Attributes;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class ScreenTranslatorProperties
    {
        [CmdConfigureIgnore]
        public HotkeySettings DefaultActivationShortcut => new HotkeySettings(true, false, true, false, 0x54); // Win+Ctrl+T

        [CmdConfigureIgnore]
        public HotkeySettings DefaultCurrentScreenShortcut => new HotkeySettings(true, true, false, true, 0x54); // Win+Ctrl+Shift+T

        [CmdConfigureIgnore]
        public HotkeySettings DefaultActiveWindowShortcut => new HotkeySettings(true, true, true, false, 0x54); // Win+Ctrl+Alt+T

        [CmdConfigureIgnore]
        public HotkeySettings DefaultScanTextShortcut => new HotkeySettings(true, false, true, true, 0x54); // Win+Alt+Shift+T

        public ScreenTranslatorProperties()
        {
            ActivationShortcut = DefaultActivationShortcut;
            CurrentScreenShortcut = DefaultCurrentScreenShortcut;
            ActiveWindowShortcut = DefaultActiveWindowShortcut;
            ScanTextShortcut = DefaultScanTextShortcut;
            SelectedProvider = "Passthrough";
            OcrProvider = "Automatic";
            EnableCloudConsent = false;
            FreezeCapturedContent = false;
            SourceLanguage = "auto";
            TargetLanguage = "en-US";
            SecondaryTargetLanguage = "zh-Hans";
            AzureEndpoint = "https://api.cognitive.microsofttranslator.com";
            AzureRegion = string.Empty;
            AzureVisionEndpoint = string.Empty;
            LibreTranslateEndpoint = "http://localhost:5000";
            AcpAgentCommand = "copilot --acp --model gpt-5-mini --reasoning-effort minimal";
        }

        [JsonPropertyName("ActivationShortcut")]
        public HotkeySettings ActivationShortcut { get; set; }

        [JsonPropertyName("CurrentScreenShortcut")]
        public HotkeySettings CurrentScreenShortcut { get; set; }

        [JsonPropertyName("ActiveWindowShortcut")]
        public HotkeySettings ActiveWindowShortcut { get; set; }

        [JsonPropertyName("ScanTextShortcut")]
        public HotkeySettings ScanTextShortcut { get; set; }

        [JsonPropertyName("SelectedProvider")]
        public string SelectedProvider { get; set; }

        [JsonPropertyName("OcrProvider")]
        public string OcrProvider { get; set; }

        [JsonPropertyName("EnableCloudConsent")]
        public bool EnableCloudConsent { get; set; }

        [JsonPropertyName("FreezeCapturedContent")]
        public bool FreezeCapturedContent { get; set; }

        [JsonPropertyName("SourceLanguage")]
        public string SourceLanguage { get; set; }

        [JsonPropertyName("TargetLanguage")]
        public string TargetLanguage { get; set; }

        [JsonPropertyName("SecondaryTargetLanguage")]
        public string SecondaryTargetLanguage { get; set; }

        [JsonPropertyName("AzureEndpoint")]
        public string AzureEndpoint { get; set; }

        [JsonPropertyName("AzureRegion")]
        public string AzureRegion { get; set; }

        [JsonPropertyName("AzureVisionEndpoint")]
        public string AzureVisionEndpoint { get; set; }

        [JsonPropertyName("LibreTranslateEndpoint")]
        public string LibreTranslateEndpoint { get; set; }

        [JsonPropertyName("AcpAgentCommand")]
        public string AcpAgentCommand { get; set; }

        public override string ToString()
            => JsonSerializer.Serialize(this, SettingsSerializationContext.Default.ScreenTranslatorProperties);
    }
}
