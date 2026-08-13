// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media.Imaging;

using WorkspacesLauncherUI.Data;

namespace WorkspacesLauncherUI.Models
{
    /// <summary>
    /// Model representing an application's launch status in the Launcher UI.
    /// Drives the display of the spinner (Loading), checkmark/X glyph (StateGlyph),
    /// and color (StateColor) for each app row.
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
            get => LaunchState switch
            {
                LaunchingState.LaunchedAndMoved => ResourceLoaderInstance.ResourceLoader?.GetString("LaunchSucceededAutomationName") ?? "Launch succeeded",
                LaunchingState.Failed => ResourceLoaderInstance.ResourceLoader?.GetString("LaunchFailedAutomationName") ?? "Launch failed",
                LaunchingState.Canceled => ResourceLoaderInstance.ResourceLoader?.GetString("LaunchCanceledAutomationName") ?? "Launch canceled",
                _ => ResourceLoaderInstance.ResourceLoader?.GetString("LaunchInProgressAutomationName") ?? "Launching",
            };
        }
    }
}
