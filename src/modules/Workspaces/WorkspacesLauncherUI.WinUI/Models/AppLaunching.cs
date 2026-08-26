// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media.Imaging;

using WorkspacesLauncherUI.Data;

namespace WorkspacesLauncherUI.Models
{
    /// <summary>
    /// Model representing an application's launch status in the Launcher UI.
    /// Exposes the loading and state values used by StatusPage.xaml to select
    /// the spinner, status glyph, theme brush, and accessibility announcement.
    /// </summary>
    public partial class AppLaunching : ObservableObject
    {
        public bool Loading => LaunchState == LaunchingState.Waiting || LaunchState == LaunchingState.Launched;

        public string Name { get; set; }

        public string AppPath { get; set; }

        public BitmapImage IconImage { get; set; }

        public string PackagedName { get; set; }

        public string Aumid { get; set; }

        public string PwaAppId { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Loading))]
        [NotifyPropertyChangedFor(nameof(LaunchStateInt))]
        [NotifyPropertyChangedFor(nameof(StateAutomationName))]
        private LaunchingState _launchState;

        public int LaunchStateInt => (int)LaunchState;

        public string StateAutomationName
        {
            get
            {
                string format = LaunchState switch
                {
                    LaunchingState.LaunchedAndMoved => GetResourceOrDefault("LaunchSucceededAutomationName", "{0}: launch succeeded"),
                    LaunchingState.Failed => GetResourceOrDefault("LaunchFailedAutomationName", "{0}: launch failed"),
                    LaunchingState.Canceled => GetResourceOrDefault("LaunchCanceledAutomationName", "{0}: launch canceled"),
                    _ => GetResourceOrDefault("LaunchInProgressAutomationName", "{0}: launching"),
                };

                return string.Format(CultureInfo.CurrentCulture, format, Name);
            }
        }

        private static string GetResourceOrDefault(string resourceName, string fallback)
        {
            string value = ResourceLoaderInstance.ResourceLoader?.GetString(resourceName);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
