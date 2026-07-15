// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SamplePagesExtension;

/// <summary>
/// Monitors OS shutdown and session-end messages by running a Windows message
/// loop on a dedicated STA thread with a hidden window. Without this monitor,
/// a WinExe COM server that blocks on <see cref="ManualResetEvent.WaitOne"/>
/// never processes <c>WM_QUERYENDSESSION</c> or <c>WM_ENDSESSION</c>, causing
/// MOAPPLICATION_HANG / HANG_QUIESCE WER reports on OS-initiated shutdown.
/// </summary>
/// <remarks>
/// The window must be created as <c>WS_POPUP</c> rather than a message-only
/// window (<c>HWND_MESSAGE</c>) because message-only windows do not receive
/// <c>WM_QUERYENDSESSION</c> or <c>WM_ENDSESSION</c>.
/// </remarks>
internal sealed class AppLifeMonitor : IDisposable
{
    private readonly ManualResetEvent _exitRequestedEvent = new(false);
    private readonly ManualResetEvent _threadReadyEvent = new(false);
    private Thread? _messageLoopThread;
    private uint _messageThreadId;
    private bool _disposed;

    /// <summary>
    /// Gets a <see cref="WaitHandle"/> that is signaled when the OS requests
    /// process termination (shutdown, logoff, or session end).
    /// </summary>
    public WaitHandle ExitRequestedWaitHandle => _exitRequestedEvent;

    /// <summary>Starts the background message-loop thread.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(AppLifeMonitor));

        _messageLoopThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "AppLifeMonitor",
        };
        _messageLoopThread.SetApartmentState(ApartmentState.STA);
        _messageLoopThread.Start();

        // Wait until the message-loop thread has initialised its window.
        _threadReadyEvent.WaitOne();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_messageThreadId != 0)
        {
            NativeMethods.PostThreadMessageW(_messageThreadId, NativeMethods.WM_QUIT, 0, 0);
        }

        _messageLoopThread?.Join(TimeSpan.FromSeconds(5));
        _messageLoopThread = null;

        _exitRequestedEvent.Dispose();
        _threadReadyEvent.Dispose();
    }

    private void RunMessageLoop()
    {
        _messageThreadId = NativeMethods.GetCurrentThreadId();
        string className = $"AppLifeMonitor_{_messageThreadId}";
        nint hInstance = NativeMethods.GetModuleHandleW(null);

        // Keep the delegate alive for the duration of the message loop so it
        // is not collected by the GC while native code holds a reference to it.
        NativeMethods.WNDPROC wndProcDelegate = WndProcCallback;
        nint wndProcPtr = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);

        var wndClass = new NativeMethods.WNDCLASSW
        {
            lpfnWndProc = wndProcPtr,
            hInstance = hInstance,
            lpszClassName = className,
        };

        ushort atom = NativeMethods.RegisterClassW(ref wndClass);

        // Signal Start() even on failure so it is never left waiting forever.
        _threadReadyEvent.Set();

        if (atom == 0)
        {
            return;
        }

        nint hwnd = NativeMethods.CreateWindowExW(
            dwExStyle: 0,
            lpClassName: className,
            lpWindowName: "AppLifeMonitor",
            dwStyle: NativeMethods.WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: 0, hMenu: 0, hInstance: hInstance, lpParam: 0);

        while (NativeMethods.GetMessageW(out NativeMethods.MSG msg, hWnd: 0, wMsgFilterMin: 0, wMsgFilterMax: 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (hwnd != 0)
        {
            NativeMethods.DestroyWindow(hwnd);
        }

        NativeMethods.UnregisterClassW(className, hInstance);

        // Keep the delegate alive until after the message loop has fully exited.
        GC.KeepAlive(wndProcDelegate);
    }

    private nint WndProcCallback(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_QUERYENDSESSION:
                // Tell Windows we are ready to quit so it does not report us as
                // hung (MOAPPLICATION_HANG / HANG_QUIESCE), and signal the main
                // thread so it can clean up and exit promptly.
                _exitRequestedEvent.Set();
                return 1; // TRUE: ready to end the session

            case NativeMethods.WM_ENDSESSION:
                if (wParam != 0)
                {
                    _exitRequestedEvent.Set();
                }

                return 0;
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static class NativeMethods
    {
        internal const uint WM_QUIT = 0x0012;
        internal const uint WM_QUERYENDSESSION = 0x0011;
        internal const uint WM_ENDSESSION = 0x0016;
        internal const uint WS_POPUP = 0x80000000;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate nint WNDPROC(nint hwnd, uint msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WNDCLASSW
        {
            public uint style;
            public nint lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG
        {
            public nint hwnd;
            public uint message;
            public nint wParam;
            public nint lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterClassW(string lpClassName, nint hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            nint hWndParent,
            nint hMenu,
            nint hInstance,
            nint lpParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll")]
        internal static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        internal static extern nint DispatchMessageW(ref MSG lpMsg);

        [DllImport("user32.dll")]
        internal static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint GetModuleHandleW(string? lpModuleName);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostThreadMessageW(uint idThread, uint Msg, nint wParam, nint lParam);
    }
}
