// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace KeyboardManagerEditorUI
{
    public static partial class Program
    {
        private const string InstanceMutexName = "Local\\PowerToys_KBMEditor_InstanceMutex";

        [STAThread]
        public static int Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            // Share the legacy editor's mutex so both editor implementations cannot reset the engine event concurrently.
            using var instanceMutex = new Mutex(false, InstanceMutexName);
            bool ownsInstanceMutex;
            try
            {
                ownsInstanceMutex = instanceMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsInstanceMutex = true;
            }

            if (!ownsInstanceMutex)
            {
                return 0;
            }

            try
            {
                Application.Start(p =>
                {
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    _ = new App();
                });
            }
            finally
            {
                instanceMutex.ReleaseMutex();
            }

            return 0;
        }
    }
}
