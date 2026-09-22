// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.UI.Dispatching;
using ScreenTranslator.Helpers;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace ScreenTranslator.Keyboard;

public sealed class KeyboardMonitor : IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly KeyboardShortcutRouter _shortcutRouter = new(IsKeyDown);
    private GlobalKeyboardHook? _keyboardHook;

    public KeyboardMonitor()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public void Start()
    {
        _keyboardHook = new GlobalKeyboardHook();
        _keyboardHook.KeyboardPressed += Hook_KeyboardPressed;
    }

    private void Hook_KeyboardPressed(object? sender, GlobalKeyboardHookEventArgs e)
    {
        if (_shortcutRouter.ShouldHandleUndo(e, WindowManager.GetKeyboardShortcutContext()))
        {
            e.Handled = true;
            _dispatcherQueue.TryEnqueue(WindowManager.UndoActiveResultCard);
        }
        else if (e.Key == VirtualKey.Escape && e.IsKeyDown)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                WindowManager.CloseAllOverlays();
            });
        }
    }

    public void Dispose()
    {
        _keyboardHook?.Dispose();
        _keyboardHook = null;
        GC.SuppressFinalize(this);
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        return (OSInterop.GetAsyncKeyState((int)key) & 0x8000) != 0;
    }
}
