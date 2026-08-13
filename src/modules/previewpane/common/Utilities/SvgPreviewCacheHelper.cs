// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Common.Utilities
{
    internal static class SvgPreviewCacheHelper
    {
        private const long MaxCacheSizeBytes = 100 * 1024 * 1024;
        private static readonly TimeSpan MaxCacheEntryAge = TimeSpan.FromDays(30);
        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MaxTransientFileAge = TimeSpan.FromDays(1);
        private static readonly UTF8Encoding Utf8NoBom = new(false);
        private static readonly object MaintenanceLock = new();
        private static readonly Dictionary<string, DateTime> LastMaintenanceByFolder = new(StringComparer.OrdinalIgnoreCase);

        internal static string GetCacheFolderPath(string webView2UserDataFolder)
        {
            return Path.Combine(webView2UserDataFolder, "SvgPreviewCache");
        }

        internal static void EnsureCacheFolder(string webView2UserDataFolder)
        {
            try
            {
                Directory.CreateDirectory(webView2UserDataFolder);
                Directory.CreateDirectory(GetCacheFolderPath(webView2UserDataFolder));
                RunMaintenanceIfNeeded(webView2UserDataFolder, DateTime.UtcNow);
            }
            catch (Exception)
            {
            }
        }

        internal static string BuildCacheKey(params string[] cacheInputs)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];

            foreach (var input in cacheInputs)
            {
                var inputBytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, inputBytes.Length);
                hash.AppendData(lengthPrefix);
                hash.AppendData(inputBytes);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        internal static string GetCacheFilePath(string cacheRootFolder, string cacheKey)
        {
            return Path.Combine(cacheRootFolder, $"{cacheKey}.html");
        }

        internal static bool TryGetCacheFile(string cacheRootFolder, string cacheKey, out string cacheFilePath)
        {
            cacheFilePath = GetCacheFilePath(cacheRootFolder, cacheKey);

            try
            {
                var cacheFile = new FileInfo(cacheFilePath);
                if (!cacheFile.Exists || cacheFile.Length == 0)
                {
                    return false;
                }

                cacheFile.LastWriteTimeUtc = DateTime.UtcNow;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool TryWriteCacheFileAtomic(
            string cacheRootFolder,
            string cacheKey,
            string contents,
            out string cacheFilePath,
            long maxCacheSizeBytes = MaxCacheSizeBytes)
        {
            cacheFilePath = GetCacheFilePath(cacheRootFolder, cacheKey);
            string? temporaryFilePath = null;

            try
            {
                if (Utf8NoBom.GetByteCount(contents) > maxCacheSizeBytes)
                {
                    return false;
                }

                Directory.CreateDirectory(cacheRootFolder);
                temporaryFilePath = Path.Combine(cacheRootFolder, $"{cacheKey}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryFilePath, contents, Utf8NoBom);
                File.Move(temporaryFilePath, cacheFilePath, overwrite: true);
                PruneCache(cacheRootFolder, DateTime.UtcNow, maxCacheSizeBytes);
                return TryGetCacheFile(cacheRootFolder, cacheKey, out cacheFilePath);
            }
            catch (Exception)
            {
                return TryGetCacheFile(cacheRootFolder, cacheKey, out cacheFilePath);
            }
            finally
            {
                if (temporaryFilePath != null)
                {
                    TryDeleteFile(temporaryFilePath);
                }
            }
        }

        internal static bool TryWriteTransientFile(string webView2UserDataFolder, string contents, out string filePath)
        {
            filePath = string.Empty;

            try
            {
                var transientFolder = Path.Combine(webView2UserDataFolder, "SvgPreviewTransient");
                Directory.CreateDirectory(transientFolder);
                filePath = Path.Combine(transientFolder, $"{Guid.NewGuid():N}.html");
                File.WriteAllText(filePath, contents, Utf8NoBom);
                return true;
            }
            catch (Exception)
            {
                TryDeleteFile(filePath);
                filePath = string.Empty;
                return false;
            }
        }

        internal static void DeleteTransientFile(string filePath)
        {
            TryDeleteFile(filePath);
        }

        internal static void PruneCache(string cacheRootFolder, DateTime utcNow, long maxCacheSizeBytes = MaxCacheSizeBytes)
        {
            try
            {
                var cacheFiles = new List<FileInfo>();
                foreach (var filePath in Directory.EnumerateFiles(cacheRootFolder, "*.html", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var cacheFile = new FileInfo(filePath);
                        if (cacheFile.Length == 0 || utcNow - cacheFile.LastWriteTimeUtc > MaxCacheEntryAge)
                        {
                            TryDeleteFile(filePath);
                        }
                        else
                        {
                            cacheFiles.Add(cacheFile);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                long retainedBytes = 0;
                foreach (var cacheFile in cacheFiles.OrderByDescending(file => file.LastWriteTimeUtc))
                {
                    if (retainedBytes + cacheFile.Length > maxCacheSizeBytes)
                    {
                        TryDeleteFile(cacheFile.FullName);
                    }
                    else
                    {
                        retainedBytes += cacheFile.Length;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static void RunMaintenanceIfNeeded(string webView2UserDataFolder, DateTime utcNow)
        {
            lock (MaintenanceLock)
            {
                if (LastMaintenanceByFolder.TryGetValue(webView2UserDataFolder, out var lastMaintenance) &&
                    utcNow - lastMaintenance < MaintenanceInterval)
                {
                    return;
                }

                LastMaintenanceByFolder[webView2UserDataFolder] = utcNow;
            }

            foreach (var legacyFile in Directory.EnumerateFiles(webView2UserDataFolder, "*.html", SearchOption.TopDirectoryOnly))
            {
                TryDeleteFile(legacyFile);
            }

            PruneCache(GetCacheFolderPath(webView2UserDataFolder), utcNow);

            var transientFolder = Path.Combine(webView2UserDataFolder, "SvgPreviewTransient");
            if (Directory.Exists(transientFolder))
            {
                foreach (var transientFile in Directory.EnumerateFiles(transientFolder, "*.html", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (utcNow - File.GetLastWriteTimeUtc(transientFile) > MaxTransientFileAge)
                        {
                            TryDeleteFile(transientFile);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception)
            {
            }
        }
    }
}
