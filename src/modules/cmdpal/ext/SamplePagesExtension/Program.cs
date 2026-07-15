// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace SamplePagesExtension;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);

            // We are instantiating an extension instance once above, and returning it every time the callback in RegisterExtension below is called.
            // This makes sure that only one instance of SampleExtension is alive, which is returned every time the host asks for the IExtension object.
            // If you want to instantiate a new instance each time the host asks, create the new instance inside the delegate.
            SampleExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<SampleExtension, IExtension>(() => extensionInstance);
            server.Start();

            // Start the lifecycle monitor so the process responds to OS-initiated
            // shutdown requests (WM_QUERYENDSESSION / WM_ENDSESSION) promptly.
            // Without this, the COM server blocks on WaitOne indefinitely and is
            // force-terminated by the OS, producing MOAPPLICATION_HANG reports.
            using AppLifeMonitor appLifeMonitor = new();
            appLifeMonitor.Start();

            // Wait until the extension is disposed by the host OR the OS requests
            // a shutdown / session end, whichever comes first.
            WaitHandle.WaitAny([extensionDisposedEvent, appLifeMonitor.ExitRequestedWaitHandle]);
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as a Extension... exiting.");
        }
    }
}
