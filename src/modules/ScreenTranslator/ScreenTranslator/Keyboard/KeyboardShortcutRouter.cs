// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Windows.System;

namespace ScreenTranslator.Keyboard;

public sealed class KeyboardShortcutRouter
{
    private readonly Func<VirtualKey, bool> _isKeyDown;
    private readonly HashSet<VirtualKey> _pressedControlKeys = new();

    public KeyboardShortcutRouter(Func<VirtualKey, bool> isKeyDown)
    {
        _isKeyDown = isKeyDown;
    }

    public bool ShouldHandleUndo(GlobalKeyboardHookEventArgs e, KeyboardShortcutContext context)
    {
        UpdateControlState(e);

        if (e.Key != VirtualKey.Z || !e.IsKeyDown)
        {
            return false;
        }

        if (!IsExactControlOnlyShortcut())
        {
            return false;
        }

        return context.HasVisibleResultOverlay &&
               context.IsResultOverlayActive &&
               !context.IsEditingResultCard &&
               context.HasSelectedResultCard &&
               context.CanUndoSelectedResultCard;
    }

    private void UpdateControlState(GlobalKeyboardHookEventArgs e)
    {
        if (IsControlKey(e.Key))
        {
            if (e.IsKeyDown)
            {
                if (e.Key == VirtualKey.Control)
                {
                    _pressedControlKeys.Add(VirtualKey.LeftControl);
                    _pressedControlKeys.Add(VirtualKey.RightControl);
                }
                else
                {
                    _pressedControlKeys.Add(e.Key);
                }
            }
            else if (e.Key == VirtualKey.Control)
            {
                _pressedControlKeys.Clear();
            }
            else
            {
                _pressedControlKeys.Remove(e.Key);
            }
        }

        if (!(IsControlKey(e.Key) && e.IsKeyDown) && !IsPhysicalControlModifierDown())
        {
            _pressedControlKeys.Clear();
        }
    }

    private bool IsExactControlOnlyShortcut()
    {
        return IsControlModifierDown() &&
               !IsModifierDown(VirtualKey.Shift, VirtualKey.LeftShift, VirtualKey.RightShift) &&
               !IsModifierDown(VirtualKey.Menu, VirtualKey.LeftMenu, VirtualKey.RightMenu) &&
               !IsModifierDown(VirtualKey.LeftWindows, VirtualKey.RightWindows);
    }

    private bool IsControlModifierDown()
    {
        return _pressedControlKeys.Count > 0 || IsPhysicalControlModifierDown();
    }

    private bool IsPhysicalControlModifierDown()
    {
        return IsModifierDown(VirtualKey.Control, VirtualKey.LeftControl, VirtualKey.RightControl);
    }

    private bool IsModifierDown(params VirtualKey[] keys)
    {
        foreach (VirtualKey key in keys)
        {
            if (_isKeyDown(key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsControlKey(VirtualKey key)
    {
        return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl;
    }
}
