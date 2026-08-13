// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Management.Deployment;
using WorkspacesCsharpLibrary;

namespace WorkspacesLauncherUI.Helpers
{
    internal static class IconHelper
    {
        private static readonly Dictionary<string, BitmapImage> IconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object IconCacheLock = new();

        public static BitmapImage GetApplicationIcon(string path, string packageFullName, string pwaAppId)
        {
            string cacheKey = $"{packageFullName}\0{pwaAppId}\0{path}";
            lock (IconCacheLock)
            {
                if (IconCache.TryGetValue(cacheKey, out BitmapImage cachedIcon))
                {
                    return cachedIcon;
                }
            }

            string iconPath = TryGetPackageIconPath(packageFullName)
                ?? TryGetPwaIconPath(pwaAppId)
                ?? (File.Exists(path) ? path : null)
                ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Workspaces", "DefaultIcon.ico");

            BitmapImage icon = LoadIcon(iconPath) ?? LoadIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Workspaces", "DefaultIcon.ico"));
            if (icon != null)
            {
                lock (IconCacheLock)
                {
                    IconCache[cacheKey] = icon;
                }
            }

            return icon;
        }

        private static string TryGetPackageIconPath(string packageFullName)
        {
            if (string.IsNullOrEmpty(packageFullName))
            {
                return null;
            }

            try
            {
                var package = new PackageManager().FindPackageForUser(string.Empty, packageFullName);
                string logoPath = package?.Logo?.LocalPath;
                if (package == null || string.IsNullOrEmpty(logoPath))
                {
                    return null;
                }

                return Path.Combine(package.InstalledLocation.Path, logoPath.TrimStart('/', '\\'));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string TryGetPwaIconPath(string pwaAppId)
        {
            if (string.IsNullOrEmpty(pwaAppId))
            {
                return null;
            }

            string path = PwaHelper.GetPwaIconFilename(pwaAppId);
            return File.Exists(path) ? path : null;
        }

        private static BitmapImage LoadIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                string extension = Path.GetExtension(path);
                if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    using Icon sourceIcon = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                        ? Icon.ExtractAssociatedIcon(path)
                        : new Icon(path);
                    using Bitmap bitmap = sourceIcon?.ToBitmap();
                    return bitmap == null ? null : ConvertToBitmapImage(bitmap);
                }

                using Image image = Image.FromFile(path);
                return ConvertToBitmapImage(image);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static BitmapImage ConvertToBitmapImage(Image image)
        {
            try
            {
                using MemoryStream stream = new();
                image.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                BitmapImage bitmapImage = new();
                bitmapImage.SetSource(stream.AsRandomAccessStream());
                return bitmapImage;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
