// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Management;
using System.Security.Principal;

namespace ManagedCommon
{
    public delegate void ThemeChangedEvent(ThemeListener sender);

    public delegate void SystemThemeChangedEvent(ThemeListener sender);

    // Mirrors common/Themes/theme_listener.h used by the Runner tray icon.
    public partial class ThemeListener : IDisposable
    {
        public AppTheme AppTheme { get; private set; }

        public AppTheme SystemTheme { get; private set; }

        public event ThemeChangedEvent ThemeChanged;

        public event SystemThemeChangedEvent SystemThemeChanged;

        private readonly ManagementEventWatcher appThemeWatcher;
        private readonly ManagementEventWatcher systemThemeWatcher;

        public ThemeListener()
        {
            var currentUser = WindowsIdentity.GetCurrent();
            var keyPath = $"{currentUser.User.Value}\\\\{ThemeHelpers.HkeyWindowsPersonalizeTheme.Replace("\\", "\\\\")}";

            var appThemeQuery = new WqlEventQuery(
                $"SELECT * FROM RegistryValueChangeEvent WHERE Hive='HKEY_USERS' AND " +
                $"KeyPath='{keyPath}' AND ValueName='{ThemeHelpers.HValueAppTheme}'");
            var newAppThemeWatcher = new ManagementEventWatcher(appThemeQuery);

            var systemThemeQuery = new WqlEventQuery(
                $"SELECT * FROM RegistryValueChangeEvent WHERE Hive='HKEY_USERS' AND " +
                $"KeyPath='{keyPath}' AND ValueName='{ThemeHelpers.HValueSystemTheme}'");
            var newSystemThemeWatcher = new ManagementEventWatcher(systemThemeQuery);

            try
            {
                newAppThemeWatcher.EventArrived += OnAppThemeRegistryChanged;
                newSystemThemeWatcher.EventArrived += OnSystemThemeRegistryChanged;

                newAppThemeWatcher.Start();
                newSystemThemeWatcher.Start();

                AppTheme = ThemeHelpers.GetAppTheme();
                SystemTheme = ThemeHelpers.GetSystemTheme();

                appThemeWatcher = newAppThemeWatcher;
                systemThemeWatcher = newSystemThemeWatcher;
            }
            catch
            {
                newAppThemeWatcher.EventArrived -= OnAppThemeRegistryChanged;
                newSystemThemeWatcher.EventArrived -= OnSystemThemeRegistryChanged;
                newAppThemeWatcher.Dispose();
                newSystemThemeWatcher.Dispose();
                throw;
            }
        }

        private void OnAppThemeRegistryChanged(object sender, EventArrivedEventArgs e)
        {
            var appTheme = ThemeHelpers.GetAppTheme();
            if (appTheme != AppTheme)
            {
                AppTheme = appTheme;
                ThemeChanged?.Invoke(this);
            }
        }

        private void OnSystemThemeRegistryChanged(object sender, EventArrivedEventArgs e)
        {
            var systemTheme = ThemeHelpers.GetSystemTheme();
            if (systemTheme != SystemTheme)
            {
                SystemTheme = systemTheme;
                SystemThemeChanged?.Invoke(this);
            }
        }

        public void Dispose()
        {
            appThemeWatcher.Dispose();
            systemThemeWatcher.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
