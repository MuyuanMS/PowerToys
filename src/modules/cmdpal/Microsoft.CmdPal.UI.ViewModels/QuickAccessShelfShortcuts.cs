// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Windows.System;

namespace Microsoft.CmdPal.UI.ViewModels;

public static class QuickAccessShelfShortcuts
{
    public const int NumberedShortcutCount = 9;

    public enum SelectionShortcutTarget
    {
        None,
        Visible,
        Unavailable,
    }

    public static int GetTopRowShortcutIndex(VirtualKey key)
    {
        var index = (int)key - (int)VirtualKey.Number1;
        return index is >= 0 and < NumberedShortcutCount ? index : -1;
    }

    public static bool IsSelectionAccessKey(bool ctrl, bool shift, bool win) =>
        shift &&
        !ctrl &&
        !win;

    public static bool IsSelectionShortcut(VirtualKey key, bool ctrl, bool alt, bool shift, bool win, bool isKeyTipDisplayMode = false) =>
        (alt || isKeyTipDisplayMode) &&
        IsSelectionAccessKey(ctrl, shift, win) &&
        GetTopRowShortcutIndex(key) >= 0;

    public static SelectionShortcutTarget ResolveSelectionShortcut(
        VirtualKey key,
        bool ctrl,
        bool alt,
        bool shift,
        bool win,
        int visibleItemCount,
        bool isKeyTipDisplayMode = false)
    {
        if (!IsSelectionShortcut(key, ctrl, alt, shift, win, isKeyTipDisplayMode))
        {
            return SelectionShortcutTarget.None;
        }

        return GetTopRowShortcutIndex(key) < visibleItemCount
            ? SelectionShortcutTarget.Visible
            : SelectionShortcutTarget.Unavailable;
    }
}
