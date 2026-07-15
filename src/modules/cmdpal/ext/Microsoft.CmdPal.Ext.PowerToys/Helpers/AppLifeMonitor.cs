// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace PowerToysExtension.Helpers;

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
    // Static reference used by the [UnmanagedCallersOnly] WndProc.
    // Only one AppLifeMonitor is expected per process.
    // Marked volatile so the message-loop thread always sees the latest value
    // written by Start() / Dispose() on the main thread.
    private static volatile ManualResetEvent? s_exitEvent;

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
        s_exitEvent = _exitRequestedEvent;

        _messageLoopThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "AppLifeMonitor",
        };
        _messageLoopThread.SetApartmentState(ApartmentState.STA);
        _messageLoopThread.Start();

        // Wait until the message-loop thread has initialized its window.
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
        s_exitEvent = null;

        if (_messageThreadId != 0)
        {
            PostThreadMessageW(_messageThreadId, WM_QUIT, 0, 0);
        }

        _messageLoopThread?.Join(TimeSpan.FromSeconds(5));
        _messageLoopThread = null;

        _exitRequestedEvent.Dispose();
        _threadReadyEvent.Dispose();
    }

    private unsafe void RunMessageLoop()
    {
        _messageThreadId = GetCurrentThreadId();
        string className = $"AppLifeMonitor_{_messageThreadId}";
        nint hInstance = GetModuleHandleW(null);

        ushort atom;
        fixed (char* pClassName = className)
        {
            var wndClass = new WNDCLASSW
            {
                lpfnWndProc = &WndProc,
                hInstance = hInstance,
                lpszClassName = pClassName,
            };

            atom = RegisterClassW(in wndClass);
        }

        // Signal Start() even on failure so it is never left waiting forever.
        _threadReadyEvent.Set();

        if (atom == 0)
        {
            return;
        }

        // Use WS_POPUP rather than HWND_MESSAGE so that the window receives
        // WM_QUERYENDSESSION and WM_ENDSESSION from the system.
        nint hwnd = CreateWindowExW(
            dwExStyle: 0,
            lpClassName: className,
            lpWindowName: "AppLifeMonitor",
            dwStyle: WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: 0, hMenu: 0, hInstance: hInstance, lpParam: 0);

        if (hwnd == 0)
        {
            UnregisterClassW(className, hInstance);
            return;
        }

        while (GetMessageW(out MSG msg, hWnd: 0, wMsgFilterMin: 0, wMsgFilterMax: 0) > 0)
        {
            TranslateMessage(in msg);
            DispatchMessageW(in msg);
        }

        DestroyWindow(hwnd);
        UnregisterClassW(className, hInstance);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_QUERYENDSESSION:
                // Tell Windows we are ready to quit so it does not report us as
                // hung (MOAPPLICATION_HANG / HANG_QUIESCE), and signal the main
                // thread so it can clean up and exit promptly.
                // Capture the reference into a local to avoid a race between
                // the null-check and Set() if Dispose() runs concurrently.
                var queryEvt = s_exitEvent;
                queryEvt?.Set();
                return 1; // TRUE: ready to end the session

            case WM_ENDSESSION:
                if (wParam != 0)
                {
                    var endEvt = s_exitEvent;
                    endEvt?.Set();
                }

                return 0;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Win32 constants
    private const uint WM_QUIT = 0x0012;
    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;
    private const uint WS_POPUP = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WNDCLASSW
    {
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern unsafe ushort RegisterClassW(in WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
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
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(in MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern unsafe nint DefWindowProcW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint idThread, uint Msg, nint wParam, nint lParam);
}
