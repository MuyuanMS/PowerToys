// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Microsoft.PowerToys.Settings.UI.Views
{
    /// <summary>
    /// Shared confirmation dialog for every Power Display action that can damage hardware
    /// or leave a monitor in a non-recoverable state. The caller picks a
    /// <see cref="PowerDisplayWarningKind"/> and the dialog renders a warning InfoBar
    /// (title from <c>PowerDisplay_Warning_{Kind}_InfoBar</c>), a body paragraph with
    /// bullets (from <c>PowerDisplay_Warning_{Kind}_Body</c>), and a shared
    /// title / learn-more hyperlink / Enable + Cancel buttons.
    /// </summary>
    public sealed partial class PowerDisplayWarningDialog : ContentDialog
    {
        private const string DefaultBodyKey = "PowerDisplay_Warning_Default_Body";
        private const string DefaultInfoBarKey = "PowerDisplay_Warning_Default_InfoBar";
        private const string LearnMoreKey = "PowerDisplay_Warning_LearnMore";

        // Shared across every variant; not localized.
        private const string LearnMoreUrl = "https://aka.ms/powerToysOverview_PowerDisplay_Note";

        private const string BulletPrefix = "• ";

        private static readonly ResourceMap ResourceMap =
            new ResourceManager("PowerToys.Settings.pri").MainResourceMap.GetSubtree("Resources");

        public PowerDisplayWarningDialog(PowerDisplayWarningKind kind)
        {
            InitializeComponent();

            // Shared chrome: same title, hyperlink, and buttons on every variant.
            Title = GetResourceString("PowerDisplay_Warning_Title");
            PrimaryButtonText = GetResourceString("PowerDisplay_Dialog_Enable");
            CloseButtonText = GetResourceString("PowerDisplay_Dialog_Cancel");

            var learnMoreText = GetResourceString(LearnMoreKey);
            LearnMoreLink.Content = learnMoreText;
            LearnMoreLink.NavigateUri = new Uri(LearnMoreUrl);
            AutomationProperties.SetName(LearnMoreLink, learnMoreText);

            // Variant-specific content. The resw key pair is derived from the enum name so
            // adding a new warning is one enum value + two resw entries — no code change here.
            var prefix = $"PowerDisplay_Warning_{kind}";
            WarningInfoBar.Title = GetResourceString($"{prefix}_InfoBar", DefaultInfoBarKey);

            var (description, bulletItems) = SplitBodyAndBullets(GetResourceString($"{prefix}_Body", DefaultBodyKey));
            WarningDescription.Text = description;
            WarningDescription.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
            WarningList.ItemsSource = bulletItems;
            WarningList.Visibility = bulletItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string GetResourceString(string resourceKey, string fallbackResourceKey = null)
        {
            var resource = ResourceMap.TryGetValue(resourceKey)?.ValueAsString;
            if (!string.IsNullOrWhiteSpace(resource))
            {
                return resource;
            }

            return string.IsNullOrWhiteSpace(fallbackResourceKey)
                ? string.Empty
                : ResourceMap.TryGetValue(fallbackResourceKey)?.ValueAsString ?? string.Empty;
        }

        private static (string Description, List<string> BulletItems) SplitBodyAndBullets(string body)
        {
            var bulletItems = new List<string>();
            if (string.IsNullOrWhiteSpace(body))
            {
                return (string.Empty, bulletItems);
            }

            var normalizedBody = body.Replace("\r\n", "\n", StringComparison.Ordinal);
            var sections = normalizedBody.Split(new[] { "\n\n" }, 2, StringSplitOptions.None);
            var description = sections[0].Trim();

            if (sections.Length == 1)
            {
                return (description, bulletItems);
            }

            foreach (var line in sections[1].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var bullet = line.Trim();
                if (bullet.StartsWith(BulletPrefix, StringComparison.Ordinal))
                {
                    bulletItems.Add(bullet);
                }
            }

            return (description, bulletItems);
        }
    }
}
