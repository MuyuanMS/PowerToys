// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.Messaging;
using ManagedCommon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;
using WinUIEx;
using WorkspacesEditor.Helpers;
using WorkspacesEditor.Messages;
using WorkspacesEditor.Models;
using WorkspacesEditor.Views;

namespace WorkspacesEditor
{
    public sealed partial class MainWindow : WindowEx, IDisposable
    {
        public const double MinWindowWidth = 750;
        public const double MinWindowHeight = 680;

        private readonly CancellationTokenSource _cancellationToken = new();
        private RectInt32 _lastNormalBounds;

        private static string WindowPlacementPath => Path.Combine(WorkspacesCsharpLibrary.Utils.FolderUtils.DataFolder(), "editor-window-placement.json");

        public MainWindow()
        {
            this.InitializeComponent();
            MinWidth = MinWindowWidth;
            MinHeight = MinWindowHeight;

            var hwnd = WindowNative.GetWindowHandle(this);

            RestoreWindowPlacement();
            AppWindow.Changed += AppWindow_Changed;

            AppWindow.SetIcon("Assets/Workspaces/Workspaces.ico");

            // Set title from resource or fallback
            try
            {
                this.Title = ResourceLoaderInstance.ResourceLoader?.GetString("MainTitle") ?? "Workspaces";
            }
            catch
            {
                this.Title = "Workspaces";
            }

            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            SetTitleBar(AppTitleBar);
            AppTitleBar.Title = this.Title;

            this.Closed += OnClosed;

            // Listen for hotkey toggle event
            StartHotkeyEventLoop(hwnd);

            // Wire ViewModel navigation via messenger
            // Use StrongReferenceMessenger for MainWindow since Window is not rooted
            // in the visual tree and WeakReferenceMessenger may GC the registration.
            var vm = App.MainViewModel;
            StrongReferenceMessenger.Default.Register<NavigateToEditorMessage>(this, (r, m) =>
            {
                bool replacingEditorPage = ContentFrame.Content is Views.WorkspacesEditorPage;
                ContentFrame.Navigate(typeof(Views.WorkspacesEditorPage), (vm, m.Project));
                if (replacingEditorPage && ContentFrame.BackStack.Count > 0)
                {
                    ContentFrame.BackStack.RemoveAt(ContentFrame.BackStack.Count - 1);
                }

                AppTitleBar.IsBackButtonVisible = true;
                AppTitleBar.Title = m.Project.EditorWindowTitle;
            });
            StrongReferenceMessenger.Default.Register<GoBackMessage>(this, (r, m) =>
            {
                if (ContentFrame.CanGoBack)
                {
                    ContentFrame.GoBack();
                }

                AppTitleBar.IsBackButtonVisible = false;
                AppTitleBar.Title = this.Title;
            });
            StrongReferenceMessenger.Default.Register<MinimizeWindowMessage>(this, (r, m) =>
            {
                ShowWindow(WindowNative.GetWindowHandle(this), 6); // SW_MINIMIZE
            });
            StrongReferenceMessenger.Default.Register<RestoreWindowMessage>(this, (r, m) =>
            {
                ShowWindow(WindowNative.GetWindowHandle(this), 9); // SW_RESTORE
            });

            // Listen for snapshot window requests from ViewModel
            OverlayBorder overlayBorder = null;
            StrongReferenceMessenger.Default.Register<ShowSnapshotWindowMessage>(this, (r, m) =>
            {
                // Show red border overlay around all displays
                var displays = OverlayBorder.GetAllMonitorBounds();
                overlayBorder = OverlayBorder.CreateForAllMonitors(displays);

                var snapshotWindow = new Views.SnapshotWindow();
                snapshotWindow.Closed += (s, args) =>
                {
                    overlayBorder?.Dispose();
                    overlayBorder = null;
                };
                snapshotWindow.Activate();
            });

            // Bind loading ring to ViewModel.IsLoading
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.IsLoading))
                {
                    LoadingRing.IsActive = vm.IsLoading;
                    LoadingRing.Visibility = vm.IsLoading
                        ? Microsoft.UI.Xaml.Visibility.Visible
                        : Microsoft.UI.Xaml.Visibility.Collapsed;
                }
            };

            // Navigate to main page
            ContentFrame.Navigate(typeof(Views.MainPage), vm);

            Microsoft.PowerToys.Telemetry.PowerToysTelemetry.Log.WriteEvent(new Telemetry.WorkspacesEditorStartFinishEvent() { TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        }

        private void AppTitleBar_BackRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
        {
            // Discard any in-progress edits (same behavior as the editor's Cancel), then return to the overview.
            WorkspacesCsharpLibrary.Data.TempProjectData.DeleteTempFile();
            App.MainViewModel.SwitchToMainView();
        }

        private void StartHotkeyEventLoop(IntPtr hwnd)
        {
            var token = _cancellationToken.Token;
            new Thread(() =>
            {
                var eventHandle = new EventWaitHandle(false, EventResetMode.AutoReset, PowerToys.Interop.Constants.WorkspacesHotkeyEvent());
                while (true)
                {
                    if (WaitHandle.WaitAny(new WaitHandle[] { token.WaitHandle, eventHandle }) == 1)
                    {
                        App.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (ApplicationIsInFocus())
                            {
                                StrongReferenceMessenger.Default.Send(new CloseApplicationMessage());
                            }
                            else
                            {
                                if (IsIconic(hwnd))
                                {
                                    ShowWindow(hwnd, 9); // SW_RESTORE
                                }

                                WindowHelpers.BringToForeground(hwnd);
                            }
                        });
                    }
                    else
                    {
                        return;
                    }
                }
            }) { IsBackground = true }.Start();
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            SaveWindowPlacement();
            _cancellationToken.Cancel();
            _cancellationToken.Dispose();
            (Microsoft.UI.Xaml.Application.Current as IDisposable)?.Dispose();
            Microsoft.UI.Xaml.Application.Current.Exit();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (sender.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Restored)
            {
                _lastNormalBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
            }
        }

        private void RestoreWindowPlacement()
        {
            try
            {
                if (File.Exists(WindowPlacementPath))
                {
                    WindowPlacement placement = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(WindowPlacementPath));
                    if (placement != null && placement.Width >= MinWindowWidth && placement.Height >= MinWindowHeight)
                    {
                        var center = new PointInt32(placement.X + (placement.Width / 2), placement.Y + (placement.Height / 2));
                        DisplayArea display = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest);
                        RectInt32 workArea = display.WorkArea;
                        var bounds = new RectInt32(placement.X, placement.Y, placement.Width, placement.Height);
                        bool intersects = bounds.X < workArea.X + workArea.Width &&
                                          bounds.X + bounds.Width > workArea.X &&
                                          bounds.Y < workArea.Y + workArea.Height &&
                                          bounds.Y + bounds.Height > workArea.Y;
                        if (intersects)
                        {
                            AppWindow.MoveAndResize(bounds);
                            _lastNormalBounds = bounds;
                            if (placement.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
                            {
                                presenter.Maximize();
                            }

                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to restore Workspaces Editor window placement: {ex.Message}");
            }

            this.CenterOnScreen();
            _lastNormalBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        }

        private void SaveWindowPlacement()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(WindowPlacementPath));
                bool maximized = AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Maximized;
                RectInt32 bounds = _lastNormalBounds.Width > 0 ? _lastNormalBounds : new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
                File.WriteAllText(WindowPlacementPath, JsonSerializer.Serialize(new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized)));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to save Workspaces Editor window placement: {ex.Message}");
            }
        }

        private sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

        private static bool ApplicationIsInFocus()
        {
            var activatedHandle = GetForegroundWindow();
            if (activatedHandle == IntPtr.Zero)
            {
                return false;
            }

            var procId = Environment.ProcessId;
            _ = GetWindowThreadProcessId(activatedHandle, out int activeProcId);

            return activeProcId == procId;
        }

        public void Dispose()
        {
            _cancellationToken?.Dispose();
            GC.SuppressFinalize(this);
        }

        // Win32 interop
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
    }
}
