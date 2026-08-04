// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml;

namespace WorkspacesEditor.Helpers
{
    internal static class ThemeHelper
    {
        private static ElementTheme _actualTheme = ElementTheme.Default;

        internal static void TrackActualTheme(FrameworkElement root)
        {
            _actualTheme = root.ActualTheme;
            root.ActualThemeChanged += (_, _) => _actualTheme = root.ActualTheme;
        }

        /// <summary>
        /// Returns true if the current app theme is dark.
        /// Uses WinUI Application.RequestedTheme which respects system settings.
        /// </summary>
        internal static bool IsDarkTheme()
        {
            return _actualTheme == ElementTheme.Dark ||
                   (_actualTheme == ElementTheme.Default && Application.Current?.RequestedTheme == ApplicationTheme.Dark);
        }
    }
}
