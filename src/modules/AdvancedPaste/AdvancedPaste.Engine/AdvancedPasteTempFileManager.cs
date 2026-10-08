// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ManagedCommon;

namespace AdvancedPaste.Helpers;

internal static class AdvancedPasteTempFileManager
{
    private const string DirectoryPrefix = "PowerToys_AdvancedPaste_";
    private const string OwnershipMarkerName = ".powertoys-advanced-paste-owned";
    private const string OwnershipMarkerContent = "PowerToys Advanced Paste temporary directory";

    internal static DirectoryInfo CreateDirectory(string? parentDirectory = null)
    {
        var directory = parentDirectory is null
            ? Directory.CreateTempSubdirectory(DirectoryPrefix)
            : Directory.CreateDirectory(Path.Combine(parentDirectory, $"{DirectoryPrefix}{Guid.NewGuid():N}"));
        File.WriteAllText(Path.Combine(directory.FullName, OwnershipMarkerName), OwnershipMarkerContent);
        return directory;
    }

    internal static void CleanupStaleDirectories(TimeSpan maximumAge, IEnumerable<string> clipboardFilePaths, string? tempDirectoryPath = null)
    {
        var cutoff = DateTime.UtcNow - maximumAge;
        var tempDirectory = new DirectoryInfo(tempDirectoryPath ?? Path.GetTempPath());
        var protectedFilePaths = new HashSet<string>(clipboardFilePaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

        foreach (var directory in tempDirectory.EnumerateDirectories($"{DirectoryPrefix}*"))
        {
            try
            {
                if (directory.CreationTimeUtc >= cutoff || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (!HasOwnershipMarker(directory))
                {
                    continue;
                }

                var directoryPath = Path.GetFullPath(directory.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (protectedFilePaths.Any(path =>
                    path.Equals(directoryPath, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(directoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var file in directory.EnumerateFiles())
                {
                    if (!string.Equals(file.Name, OwnershipMarkerName, StringComparison.OrdinalIgnoreCase))
                    {
                        file.Delete();
                    }
                }

                RemoveOwnershipMarkerIfDirectoryWillBeEmpty(directory);
                if (!directory.EnumerateFileSystemInfos().Any())
                {
                    directory.Delete();
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Failed to clean stale Advanced Paste temporary directory: {ex.Message}");
            }
        }
    }

    internal static void RemoveOwnershipMarkerIfDirectoryWillBeEmpty(DirectoryInfo directory)
    {
        if (!HasOwnershipMarker(directory))
        {
            return;
        }

        var markerPath = Path.Combine(directory.FullName, OwnershipMarkerName);
        if (directory.EnumerateFileSystemInfos().Any(entry => !string.Equals(entry.FullName, markerPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        File.Delete(markerPath);
    }

    private static bool HasOwnershipMarker(DirectoryInfo directory)
    {
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        var markerPath = Path.Combine(directory.FullName, OwnershipMarkerName);
        var marker = new FileInfo(markerPath);
        return marker.Exists &&
               !marker.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
               File.ReadAllText(markerPath) == OwnershipMarkerContent;
    }
}
