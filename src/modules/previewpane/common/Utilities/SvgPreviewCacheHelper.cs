// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

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
                var value = input ?? string.Empty;
                BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, Utf8NoBom.GetByteCount(value));
                hash.AppendData(lengthPrefix);
                AppendUtf8(hash, value);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        internal static string GetCacheFilePath(string cacheRootFolder, string cacheKey)
        {
            return Path.Combine(cacheRootFolder, $"{cacheKey}.html");
        }

        internal static bool TryGetCacheFile(string cacheRootFolder, string cacheKey, out string cacheFilePath, out FileStream? cacheLease)
        {
            cacheFilePath = GetCacheFilePath(cacheRootFolder, cacheKey);
            cacheLease = null;

            try
            {
                cacheLease = new FileStream(cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (cacheLease.Length == 0)
                {
                    cacheLease.Dispose();
                    cacheLease = null;
                    return false;
                }

                try
                {
                    File.SetLastWriteTimeUtc(cacheFilePath, DateTime.UtcNow);
                }
                catch (Exception)
                {
                }

                return true;
            }
            catch (Exception)
            {
                cacheLease?.Dispose();
                cacheLease = null;
                return false;
            }
        }

        internal static bool TryWriteCacheFileAtomic(
            string cacheRootFolder,
            string cacheKey,
            string contents,
            out string cacheFilePath,
            out FileStream? cacheLease,
            long maxCacheSizeBytes = MaxCacheSizeBytes)
        {
            cacheFilePath = GetCacheFilePath(cacheRootFolder, cacheKey);
            cacheLease = null;
            string? temporaryFilePath = null;
            Mutex? cacheMutex = null;
            bool mutexAcquired = false;

            try
            {
                if (Utf8NoBom.GetByteCount(contents) > maxCacheSizeBytes)
                {
                    return false;
                }

                Directory.CreateDirectory(cacheRootFolder);
                cacheMutex = new Mutex(false, GetCacheMutexName(cacheRootFolder));
                try
                {
                    mutexAcquired = cacheMutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    mutexAcquired = true;
                }

                if (!mutexAcquired)
                {
                    return false;
                }

                temporaryFilePath = Path.Combine(cacheRootFolder, $"{cacheKey}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryFilePath, contents, Utf8NoBom);
                File.Move(temporaryFilePath, cacheFilePath, overwrite: true);

                if (!EnsureCacheSizeLimit(cacheRootFolder, cacheFilePath, maxCacheSizeBytes))
                {
                    return false;
                }

                return TryGetCacheFile(cacheRootFolder, cacheKey, out cacheFilePath, out cacheLease);
            }
            catch (Exception)
            {
                return TryGetCacheFile(cacheRootFolder, cacheKey, out cacheFilePath, out cacheLease);
            }
            finally
            {
                if (temporaryFilePath != null)
                {
                    TryDeleteFile(temporaryFilePath);
                }

                if (mutexAcquired)
                {
                    cacheMutex!.ReleaseMutex();
                }

                cacheMutex?.Dispose();
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

        internal static long PruneCache(string cacheRootFolder, DateTime utcNow, long maxCacheSizeBytes = MaxCacheSizeBytes)
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
                            if (!TryDeleteFile(filePath))
                            {
                                cacheFiles.Add(cacheFile);
                            }
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
                        if (!TryDeleteFile(cacheFile.FullName))
                        {
                            retainedBytes += cacheFile.Length;
                        }
                    }
                    else
                    {
                        retainedBytes += cacheFile.Length;
                    }
                }

                return retainedBytes;
            }
            catch (Exception)
            {
                return long.MaxValue;
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

            var cacheRootFolder = GetCacheFolderPath(webView2UserDataFolder);
            PruneCache(cacheRootFolder, utcNow);

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

        private static void AppendUtf8(IncrementalHash hash, string value)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);

            try
            {
                var encoder = Utf8NoBom.GetEncoder();
                ReadOnlySpan<char> remaining = value.AsSpan();
                bool completed;

                do
                {
                    encoder.Convert(remaining, buffer, flush: true, out int charsUsed, out int bytesUsed, out completed);
                    hash.AppendData(buffer.AsSpan(0, bytesUsed));
                    remaining = remaining[charsUsed..];
                }
                while (!completed);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static bool EnsureCacheSizeLimit(string cacheRootFolder, string newFilePath, long maxCacheSizeBytes)
        {
            long retainedBytes = PruneCache(cacheRootFolder, DateTime.UtcNow, maxCacheSizeBytes);
            if (retainedBytes > maxCacheSizeBytes)
            {
                TryDeleteFile(newFilePath);
                retainedBytes = PruneCache(cacheRootFolder, DateTime.UtcNow, maxCacheSizeBytes);
            }

            return retainedBytes <= maxCacheSizeBytes && File.Exists(newFilePath);
        }

        private static string GetCacheMutexName(string cacheRootFolder)
        {
            var normalizedPath = Path.GetFullPath(cacheRootFolder).ToUpperInvariant();
            var pathHash = Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(normalizedPath)));
            return $"Local\\PowerToysSvgPreviewCache_{pathHash}";
        }

        private static bool TryDeleteFile(string filePath)
        {
            try
            {
                File.Delete(filePath);
                return !File.Exists(filePath);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
