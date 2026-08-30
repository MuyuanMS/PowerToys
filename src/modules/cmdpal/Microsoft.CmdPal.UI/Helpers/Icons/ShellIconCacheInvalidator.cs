// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using ManagedCommon;

namespace Microsoft.CmdPal.UI.Helpers;

/// <summary>
/// Invalidates cached Shell identities when the system image list can be rebuilt.
/// </summary>
internal sealed partial class ShellIconCacheInvalidator : IDisposable
{
    private const int ShcnrfShellLevel = 0x0002;
    private const int ShcnrfNewDelivery = 0x8000;
    private static readonly TimeSpan DegradedRefreshInterval = TimeSpan.FromMinutes(5);
    internal const int ShcneAssocChanged = 0x08000000;
    internal const int ShcneUpdateImage = 0x00008000;
    internal const int ShellIconChangeEvents = ShcneAssocChanged | ShcneUpdateImage;

    private static readonly Guid DesktopFolderId = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    private readonly nint _windowHandle;
    private readonly uint _messageId;
    private readonly ShellIconLocationCache _locations;
    private uint _registrationId;
    private Timer? _degradedRefreshTimer;

    public ShellIconCacheInvalidator(
        nint windowHandle,
        uint messageId,
        ShellIconLocationCache locations)
    {
        _windowHandle = windowHandle;
        _messageId = messageId;
        _locations = locations;
        Register();
    }

    public bool TryHandleMessage(uint message, nint wParam, nint lParam)
    {
        if (_messageId == 0 || message != _messageId)
        {
            return false;
        }

        var notificationLock = LockNotification(wParam, lParam, out var eventId);
        if (notificationLock == 0)
        {
            _locations.Clear();
            return true;
        }

        _ = NativeMethods.SHChangeNotification_Unlock(notificationLock);
        var reason = (eventId & ShcneUpdateImage) != 0
            ? ShellIconCacheInvalidationReason.SystemImageUpdated
            : ShellIconCacheInvalidationReason.AssociationChanged;

        if ((eventId & ShcneAssocChanged) != 0)
        {
            IconLoadDiagnostics.RecordShellAssociationChangedNotification();
        }

        Invalidate(reason);
        return true;
    }

    public void Invalidate(ShellIconCacheInvalidationReason reason)
    {
        _locations.Clear();
        IconLoadDiagnostics.RecordShellIconCacheInvalidation(reason);
    }

    public void OnShellRestarted()
    {
        Invalidate(ShellIconCacheInvalidationReason.ShellRestarted);
        Deregister();
        Register();
    }

    public void Dispose() => Deregister();

    private unsafe void Register()
    {
        if (_registrationId != 0 || _windowHandle == 0 || _messageId == 0)
        {
            StopDegradedRefreshTimer();
            return;
        }

        var desktopFolderId = DesktopFolderId;
        nint desktopItemIdList = 0;
        var result = NativeMethods.SHGetKnownFolderIDList(
            &desktopFolderId,
            0,
            0,
            &desktopItemIdList);
        if (result < 0 || desktopItemIdList == 0)
        {
            Logger.LogWarning("Failed to resolve the Shell desktop for icon association notifications; Shell icon aliases will expire normally");
            StartDegradedRefreshTimer();
            return;
        }

        try
        {
            var entry = new ShellChangeNotifyEntry
            {
                ItemIdList = desktopItemIdList,
                Recursive = 1,
            };
            _registrationId = NativeMethods.SHChangeNotifyRegister(
                _windowHandle,
                ShcnrfShellLevel | ShcnrfNewDelivery,
                ShellIconChangeEvents,
                _messageId,
                1,
                &entry);
        }
        finally
        {
            Marshal.FreeCoTaskMem(desktopItemIdList);
        }

        if (_registrationId == 0)
        {
            Logger.LogWarning("Failed to register for Shell icon association changes; Shell icon aliases will expire normally");
            StartDegradedRefreshTimer();
        }
        else
        {
            StopDegradedRefreshTimer();
        }
    }

    private void Deregister()
    {
        var registrationId = _registrationId;
        _registrationId = 0;
        if (registrationId != 0)
        {
            _ = NativeMethods.SHChangeNotifyDeregister(registrationId);
        }

        StopDegradedRefreshTimer();
    }

    private void StartDegradedRefreshTimer()
    {
        if (_degradedRefreshTimer is not null)
        {
            return;
        }

        _degradedRefreshTimer = new Timer(
            _ => Invalidate(ShellIconCacheInvalidationReason.RegistrationUnavailableRefresh),
            null,
            DegradedRefreshInterval,
            DegradedRefreshInterval);
    }

    private void StopDegradedRefreshTimer()
    {
        Interlocked.Exchange(ref _degradedRefreshTimer, null)?.Dispose();
    }

    private static unsafe nint LockNotification(nint wParam, nint lParam, out int eventId)
    {
        // New-delivery notifications use Shell-owned shared memory. We do not need the
        // PIDLs for this global event, but locking and unlocking acknowledges the payload.
        var itemIdLists = stackalloc nint[2];
        var eventIdStorage = stackalloc int[1];
        eventIdStorage[0] = 0;
        var notificationLock = NativeMethods.SHChangeNotification_Lock(
            wParam,
            unchecked((uint)lParam),
            itemIdLists,
            eventIdStorage);
        eventId = eventIdStorage[0];
        return notificationLock;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellChangeNotifyEntry
    {
        public nint ItemIdList;
        public int Recursive;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("shell32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe partial int SHGetKnownFolderIDList(
            Guid* knownFolderId,
            uint flags,
            nint token,
            nint* itemIdList);

        [LibraryImport("shell32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe partial uint SHChangeNotifyRegister(
            nint windowHandle,
            int sources,
            int events,
            uint messageId,
            int entryCount,
            ShellChangeNotifyEntry* entries);

        [LibraryImport("shell32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe partial nint SHChangeNotification_Lock(
            nint changeHandle,
            uint processId,
            nint* itemIdLists,
            int* eventId);

        [LibraryImport("shell32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SHChangeNotification_Unlock(nint notificationLock);

        [LibraryImport("shell32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SHChangeNotifyDeregister(uint registrationId);
    }
}
