// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO.MemoryMappedFiles;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Helpers;

internal static class ActiveWindowSnapshotReader
{
    private const string SnapshotName = "Local\\PowerToys_ScreenTranslator_ActiveWindowSnapshot-7bb7644a-ecaa-4baa-8b79-3df430451f20";

    public static (IntPtr Handle, PhysicalRect Bounds)? TryRead()
    {
        try
        {
            using MemoryMappedFile mapping = MemoryMappedFile.OpenExisting(SnapshotName, MemoryMappedFileRights.Read);
            using MemoryMappedViewAccessor view = mapping.CreateViewAccessor(0, 28, MemoryMappedFileAccess.Read);
            long handle = view.ReadInt64(0);
            int left = view.ReadInt32(8);
            int top = view.ReadInt32(12);
            int right = view.ReadInt32(16);
            int bottom = view.ReadInt32(20);
            bool isValid = view.ReadInt32(24) != 0;
            if (!isValid || handle == 0 || right <= left || bottom <= top)
            {
                return null;
            }

            return (
                new IntPtr(handle),
                new PhysicalRect(left, top, right - left, bottom - top));
        }
        catch (Exception ex)
        {
            ManagedCommon.Logger.LogInfo($"Active-window snapshot unavailable; using current foreground window: {ex.Message}");
            return null;
        }
    }
}
