// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Foundation;
using WorkspacesCsharpLibrary.Data;
using WorkspacesEditor.Helpers;

namespace WorkspacesEditor.Models
{
    public partial class Project : ObservableObject
    {
        private CancellationTokenSource _previewRenderCancellationTokenSource;

        [JsonIgnore]
        public string EditorWindowTitle { get; set; }

        public string Id { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanBeSaved))]
        private string _name;

        public long CreationTime { get; }

        public long LastLaunchedTime { get; }

        public bool IsShortcutNeeded { get; set; }

        public bool MoveExistingWindows { get; set; }

        public string LastLaunched
        {
            get
            {
                string lastLaunched = GetString("LastLaunched") + ": ";
                if (LastLaunchedTime == 0)
                {
                    return lastLaunched + GetString("Never");
                }

                const int Second = 1;
                const int Minute = 60 * Second;
                const int Hour = 60 * Minute;
                const int Day = 24 * Hour;
                const int Month = 30 * Day;

                DateTime lastLaunchDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(LastLaunchedTime);

                TimeSpan ts = DateTime.UtcNow - lastLaunchDateTime;
                double delta = Math.Abs(ts.TotalSeconds);

                if (delta < 1 * Minute)
                {
                    return lastLaunched + GetString("Recently");
                }

                if (delta < 2 * Minute)
                {
                    return lastLaunched + GetString("OneMinuteAgo");
                }

                if (delta < 45 * Minute)
                {
                    return lastLaunched + ts.Minutes + " " + GetString("MinutesAgo");
                }

                if (delta < 90 * Minute)
                {
                    return lastLaunched + GetString("OneHourAgo");
                }

                if (delta < 24 * Hour)
                {
                    return lastLaunched + ts.Hours + " " + GetString("HoursAgo");
                }

                if (delta < 48 * Hour)
                {
                    return lastLaunched + GetString("Yesterday");
                }

                if (delta < 30 * Day)
                {
                    return lastLaunched + ts.Days + " " + GetString("DaysAgo");
                }

                if (delta < 12 * Month)
                {
                    int months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
                    return lastLaunched + (months <= 1 ? GetString("OneMonthAgo") : months + " " + GetString("MonthsAgo"));
                }
                else
                {
                    int years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
                    return lastLaunched + (years <= 1 ? GetString("OneYearAgo") : years + " " + GetString("YearsAgo"));
                }
            }
        }

        public bool CanBeSaved => !string.IsNullOrEmpty(Name) && Applications.Any(application => application.IsIncluded);

        [ObservableProperty]
        private bool _isRevertEnabled;

        [ObservableProperty]
        [property: JsonIgnore]
        private bool _isPopupVisible;

        public List<Application> Applications { get; set; }

        public List<object> ApplicationsListed
        {
            get
            {
                List<object> applicationsListed = [];
                ILookup<MonitorSetup, Application> apps = Applications.Where(x => !x.Minimized).ToLookup(x => x.MonitorSetup);
                foreach (IGrouping<MonitorSetup, Application> appItem in apps.OrderBy(x => x.Key.MonitorDpiUnawareBounds.X).ThenBy(x => x.Key.MonitorDpiUnawareBounds.Y))
                {
                    MonitorHeaderRow headerRow = new() { MonitorName = GetString("Screen") + " " + appItem.Key.MonitorNumber, SelectString = GetString("SelectAllAppsOnMonitor") + " " + appItem.Key.MonitorInfo };
                    applicationsListed.Add(headerRow);
                    foreach (Application app in appItem)
                    {
                        applicationsListed.Add(app);
                    }
                }

                IEnumerable<Application> minimizedApps = Applications.Where(x => x.Minimized);
                if (minimizedApps.Any())
                {
                    MonitorHeaderRow headerRow = new() { MonitorName = GetString("Minimized_Apps"), SelectString = GetString("SelectAllMinimizedApps") };
                    applicationsListed.Add(headerRow);
                    foreach (Application app in minimizedApps)
                    {
                        applicationsListed.Add(app);
                    }
                }

                return applicationsListed;
            }
        }

        [JsonIgnore]
        public string AppsCountString
        {
            get
            {
                int count = Applications.Count;
                return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? GetString("App") : GetString("Apps"));
            }
        }

        /// <summary>
        /// Call after modifying the Applications list to notify dependent computed properties.
        /// </summary>
        public void NotifyApplicationsChanged()
        {
            OnPropertyChanged(nameof(AppsCountString));
            OnPropertyChanged(nameof(CanBeSaved));
            OnPropertyChanged(nameof(ApplicationsListed));
        }

        /// <summary>
        /// Call to refresh the relative time display for LastLaunched.
        /// </summary>
        public void NotifyLastLaunchedChanged()
        {
            OnPropertyChanged(nameof(LastLaunched));
        }

        public List<MonitorSetup> Monitors { get; }

        public bool IsPositionChangedManually { get; set; }

        [ObservableProperty]
        [property: JsonIgnore]
        private BitmapImage _previewIcons;

        [ObservableProperty]
        [property: JsonIgnore]
        private BitmapImage _previewImage;

        [ObservableProperty]
        [property: JsonIgnore]
        private double _previewImageWidth;

        public Project()
        {
            Applications = [];
            Monitors = [];
        }

        public Project(Project selectedProject)
        {
            Id = selectedProject.Id;
            Name = selectedProject.Name;
            PreviewIcons = selectedProject.PreviewIcons;
            PreviewImage = selectedProject.PreviewImage;
            IsShortcutNeeded = selectedProject.IsShortcutNeeded;
            MoveExistingWindows = selectedProject.MoveExistingWindows;

            Monitors = [];
            foreach (MonitorSetup item in selectedProject.Monitors.OrderBy(x => x.MonitorDpiAwareBounds.X).ThenBy(x => x.MonitorDpiAwareBounds.Y))
            {
                Monitors.Add(item);
            }

            Applications = [];
            foreach (Application item in selectedProject.Applications)
            {
                Application newApp = new(item);
                newApp.Parent = this;
                newApp.InitializationFinished();
                Applications.Add(newApp);
            }
        }

        public Project(ProjectWrapper project)
        {
            Id = project.Id;
            Name = project.Name;
            CreationTime = project.CreationTime;
            LastLaunchedTime = project.LastLaunchedTime;
            IsShortcutNeeded = project.IsShortcutNeeded;
            MoveExistingWindows = project.MoveExistingWindows;
            Monitors = [];
            Applications = [];

            foreach (ApplicationWrapper app in project.Applications)
            {
                Application newApp = new()
                {
                    Id = string.IsNullOrEmpty(app.Id) ? $"{{{Guid.NewGuid()}}}" : app.Id,
                    AppName = app.Application,
                    AppPath = app.ApplicationPath,
                    AppTitle = app.Title,
                    PwaAppId = string.IsNullOrEmpty(app.PwaAppId) ? string.Empty : app.PwaAppId,
                    Version = string.IsNullOrEmpty(app.Version) ? string.Empty : app.Version,
                    PackageFullName = app.PackageFullName,
                    AppUserModelId = app.AppUserModelId,
                    Parent = this,
                    CommandLineArguments = app.CommandLineArguments,
                    IsElevated = app.IsElevated,
                    CanLaunchElevated = app.CanLaunchElevated,
                    Maximized = app.Maximized,
                    Minimized = app.Minimized,
                    IsNotFound = false,
                    Position = new Application.WindowPosition()
                    {
                        Height = app.Position.Height,
                        Width = app.Position.Width,
                        X = app.Position.X,
                        Y = app.Position.Y,
                    },
                    MonitorNumber = app.Monitor,
                };
                newApp.InitializationFinished();
                Applications.Add(newApp);
            }

            foreach (MonitorConfigurationWrapper monitor in project.MonitorConfiguration)
            {
                Rect dpiAware = new(monitor.MonitorRectDpiAware.Left, monitor.MonitorRectDpiAware.Top, monitor.MonitorRectDpiAware.Width, monitor.MonitorRectDpiAware.Height);
                Rect dpiUnaware = new(monitor.MonitorRectDpiUnaware.Left, monitor.MonitorRectDpiUnaware.Top, monitor.MonitorRectDpiUnaware.Width, monitor.MonitorRectDpiUnaware.Height);
                Monitors.Add(new MonitorSetup(monitor.Id, monitor.InstanceId, monitor.MonitorNumber, monitor.Dpi, dpiAware, dpiUnaware));
            }
        }

        public void InitializePreview()
        {
            var request = new CancellationTokenSource();
            CancellationTokenSource previousRequest = Interlocked.Exchange(ref _previewRenderCancellationTokenSource, request);
            previousRequest?.Cancel();
            _ = InitializePreviewAsync(request);
        }

        private async Task InitializePreviewAsync(CancellationTokenSource request)
        {
            try
            {
                await Task.Delay(150, request.Token);

                if (Applications == null || Applications.Count == 0 || Monitors == null || Monitors.Count == 0)
                {
                    return;
                }

                // Compute bounding rect across all monitors
                double left = Monitors.Min(m => m.MonitorDpiAwareBounds.X);
                double top = Monitors.Min(m => m.MonitorDpiAwareBounds.Y);
                double right = Monitors.Max(m => m.MonitorDpiAwareBounds.X + m.MonitorDpiAwareBounds.Width);
                double bottom = Monitors.Max(m => m.MonitorDpiAwareBounds.Y + m.MonitorDpiAwareBounds.Height);

                var bounds = new System.Drawing.Rectangle((int)left, (int)top, (int)(right - left), (int)(bottom - top));

                bool isDarkTheme = Helpers.ThemeHelper.IsDarkTheme();

                var images = await Task.Run(
                    () => (
                        Preview: Utils.DrawHelper.DrawPreview(this, bounds, isDarkTheme),
                        Icons: Utils.DrawHelper.DrawPreviewIcons(this)),
                    request.Token);

                request.Token.ThrowIfCancellationRequested();
                PreviewImage = images.Preview;
                PreviewImageWidth = bounds.Width * 0.1;
                PreviewIcons = images.Icons;
            }
            catch (OperationCanceledException)
            {
            }
            catch (System.Exception ex)
            {
                ManagedCommon.Logger.LogError("Preview render failed", ex);
            }
            finally
            {
                _ = Interlocked.CompareExchange(ref _previewRenderCancellationTokenSource, null, request);
                request.Dispose();
            }
        }

        public MonitorSetup GetMonitorForApp(Application app)
        {
            if (Monitors == null || Monitors.Count == 0)
            {
                return new MonitorSetup("Unknown", string.Empty, app.MonitorNumber, 96, default, default);
            }

            MonitorSetup monitor = Monitors.FirstOrDefault(m => m.MonitorNumber == app.MonitorNumber);
            if (monitor != null)
            {
                return monitor;
            }

            int middleX = app.Position.X + (app.Position.Width / 2);
            int middleY = app.Position.Y + (app.Position.Height / 2);
            monitor = Monitors.FirstOrDefault(candidate =>
                candidate.MonitorDpiUnawareBounds.Left < middleX &&
                candidate.MonitorDpiUnawareBounds.Right > middleX &&
                candidate.MonitorDpiUnawareBounds.Top < middleY &&
                candidate.MonitorDpiUnawareBounds.Bottom > middleY);

            monitor ??= Monitors
                .OrderBy(candidate => Math.Abs(candidate.MonitorDpiUnawareBounds.Left) + Math.Abs(candidate.MonitorDpiUnawareBounds.Top))
                .First();

            app.MonitorNumber = monitor.MonitorNumber;
            return monitor;
        }

        public void CloseExpanders()
        {
            foreach (Application app in Applications)
            {
                app.IsExpanded = false;
            }
        }

        public void UpdateAfterLaunchAndEdit(Project projectBefore)
        {
            Id = projectBefore.Id;
            Name = projectBefore.Name;
            MoveExistingWindows = projectBefore.MoveExistingWindows;
            IsRevertEnabled = true;

            foreach (Application app in Applications)
            {
                Application previousApp = projectBefore.Applications.FirstOrDefault(candidate => candidate.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
                if (previousApp != null)
                {
                    app.CommandLineArguments = previousApp.CommandLineArguments;
                    app.IsElevated = previousApp.IsElevated;
                }
            }
        }

        private static string GetString(string key)
        {
            return ResourceLoaderInstance.ResourceLoader?.GetString(key) ?? key;
        }
    }
}
