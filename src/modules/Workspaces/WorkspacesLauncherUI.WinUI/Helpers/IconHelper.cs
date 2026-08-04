// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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
        public static BitmapImage GetApplicationIcon(string path, string packageFullName, string pwaAppId)
        {
            string iconPath = TryGetPackageIconPath(packageFullName)
                ?? TryGetPwaIconPath(pwaAppId)
                ?? (File.Exists(path) ? path : null)
                ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Workspaces", "DefaultIcon.ico");

            return LoadIcon(iconPath) ?? LoadIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Workspaces", "DefaultIcon.ico"));
        }

        private static string TryGetPackageIconPath(string packageFullName)
        {
            if (string.IsNullOrEmpty(packageFullName))
            {
                return null;
            }

            try
            {
                return new PackageManager().FindPackageForUser(string.Empty, packageFullName)?.Logo?.LocalPath;
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
                using Image image = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? Icon.ExtractAssociatedIcon(path)?.ToBitmap()
                    : Image.FromFile(path);
                if (image is null)
                {
                    return null;
                }

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
