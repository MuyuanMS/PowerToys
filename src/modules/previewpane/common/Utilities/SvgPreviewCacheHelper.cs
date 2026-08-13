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
        private static readonly UTF8Encoding Utf8NoBom = new(false);

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

                foreach (var legacyFile in Directory.EnumerateFiles(webView2UserDataFolder, "*.html", SearchOption.TopDirectoryOnly))
                {
                    TryDeleteFile(legacyFile);
                }

                PruneCache(GetCacheFolderPath(webView2UserDataFolder), DateTime.UtcNow);
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

        internal static bool TryWriteCacheFileAtomic(string cacheRootFolder, string cacheKey, string contents, out string cacheFilePath)
        {
            cacheFilePath = GetCacheFilePath(cacheRootFolder, cacheKey);
            string? temporaryFilePath = null;

            try
            {
                Directory.CreateDirectory(cacheRootFolder);
                temporaryFilePath = Path.Combine(cacheRootFolder, $"{cacheKey}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryFilePath, contents, Utf8NoBom);
                File.Move(temporaryFilePath, cacheFilePath, overwrite: true);
                PruneCache(cacheRootFolder, DateTime.UtcNow);
                return true;
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

        internal static void PruneCache(string cacheRootFolder, DateTime utcNow)
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
                    if (retainedBytes + cacheFile.Length > MaxCacheSizeBytes)
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
