// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

using ManagedCommon;

namespace ClipPing;

internal sealed class VirtualDesktopManager
{
    private readonly IVirtualDesktopManager _manager;

    private VirtualDesktopManager(IVirtualDesktopManager manager)
    {
        _manager = manager;
    }

    public static VirtualDesktopManager? TryCreate()
    {
        try
        {
            return new VirtualDesktopManager((IVirtualDesktopManager)new CVirtualDesktopManager());
        }
        catch (COMException ex)
        {
            Logger.LogWarning($"Could not initialize the virtual desktop manager: {ex.Message}");
            return null;
        }
    }

    public bool TryGetWindowDesktopId(IntPtr window, out Guid desktopId)
    {
        return _manager.GetWindowDesktopId(window, out desktopId) == 0;
    }

    [ComImport]
    [Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class CVirtualDesktopManager
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [System.Security.SuppressUnmanagedCodeSecurity]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop([In] IntPtr topLevelWindow, [Out] out int onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId([In] IntPtr topLevelWindow, [Out] out Guid desktop);

        [PreserveSig]
        int MoveWindowToDesktop([In] IntPtr topLevelWindow, [MarshalAs(UnmanagedType.LPStruct)][In] Guid desktop);
    }
}
