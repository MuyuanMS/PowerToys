// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using ManagedCommon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PowerToys.Interop;

namespace WorkspacesLauncherUI
{
    /// <summary>
    /// WinUI 3 Application class for the Workspaces Launcher UI.
    /// Manages the IPC pipe connection to the C++ launcher engine and hosts the status window.
    /// </summary>
    public partial class App : Application, IDisposable
    {
        private StatusWindow _mainWindow;
        private TwoWayPipeMessageIPCManaged _ipcManager;
        private DispatcherQueueTimer _readyTimer;
        private bool _isDisposed;

        public static Action<string> IPCMessageReceivedCallback { get; set; }

        public static Action CancelAcknowledgedCallback { get; set; }

        public static DispatcherQueue DispatcherQueue { get; private set; }

        public App()
        {
            string languageTag = LanguageHelper.LoadLanguage();
            if (!string.IsNullOrEmpty(languageTag))
            {
                try
                {
                    Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageTag;
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to set language override: " + ex.Message);
                }
            }

            this.InitializeComponent();
            this.UnhandledException += OnUnhandledException;
        }

        public static void SendIPCMessage(string message)
        {
            if ((Current as App)?._ipcManager != null)
            {
                (Current as App)._ipcManager.Send(message);
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            _mainWindow = new StatusWindow();

            _ipcManager = new TwoWayPipeMessageIPCManaged(
                "\\\\.\\pipe\\powertoys_workspaces_ui_",
                "\\\\.\\pipe\\powertoys_workspaces_launcher_ui_",
                (string message) => ProcessIPCMessage(
                    message,
                    callback => DispatcherQueue.TryEnqueue(() => callback()),
                    () => _readyTimer?.Stop()));
            _ipcManager.Start();

            _readyTimer = DispatcherQueue.CreateTimer();
            _readyTimer.Interval = TimeSpan.FromMilliseconds(100);
            _readyTimer.Tick += (_, _) => SendIPCMessage("ready");
            _readyTimer.Start();
            SendIPCMessage("ready");

            _mainWindow.Activate();
        }

        internal static void ProcessIPCMessage(string message, Action<Action> enqueue, Action stopReadyTimer)
        {
            if (message == "cancel_ack")
            {
                enqueue(() => CancelAcknowledgedCallback?.Invoke());
                return;
            }

            Action<string> callback = IPCMessageReceivedCallback;
            if (callback != null && message.Length > 0)
            {
                enqueue(() =>
                {
                    stopReadyTimer();
                    callback(message);
                });
            }
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Logger.LogError("Unhandled exception occurred", e.Exception);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _readyTimer?.Stop();
                _ipcManager?.End();
                _ipcManager?.Dispose();
                _isDisposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}
