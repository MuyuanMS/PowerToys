// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ManagedCommon;
using WorkspacesCsharpLibrary.Models;
using WorkspacesLauncherUI.Data;

namespace WorkspacesLauncherUI.Models
{
    public class AppLaunching : BaseApplication, IDisposable
    {
        private BitmapImage _iconBitmapImage;

        public BitmapImage IconBitmapImage
        {
            get
            {
                if (_iconBitmapImage == null)
                {
                    try
                    {
                        using Bitmap previewBitmap = new(32, 32);
                        using (Graphics graphics = Graphics.FromImage(previewBitmap))
                        {
                            graphics.SmoothingMode = SmoothingMode.AntiAlias;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            graphics.DrawIcon(Icon, new Rectangle(0, 0, 32, 32));
                        }

                        using var memory = new MemoryStream();
                        WorkspacesCsharpLibrary.DrawHelper.SaveBitmap(previewBitmap, memory);
                        memory.Position = 0;

                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = memory;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();
                        _iconBitmapImage = bitmapImage;
                    }
                    catch (Exception)
                    {
                    }
                }

                return _iconBitmapImage;
            }
        }

        public bool Loading => LaunchState == LaunchingState.Waiting || LaunchState == LaunchingState.Launched;

        public string Name { get; set; }

        public LaunchingState LaunchState { get; set; }

        public string StateGlyph
        {
            get => LaunchState switch
            {
                LaunchingState.LaunchedAndMoved => "\U0000F78C",
                LaunchingState.Failed => "\U0000EF2C",
                _ => "\U0000EF2C",
            };
        }

        public System.Windows.Media.Brush StateColor
        {
            get => LaunchState switch
            {
                LaunchingState.LaunchedAndMoved => new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 128, 0)),
                LaunchingState.Failed => new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 254, 0, 0)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 254, 0, 0)),
            };
        }
    }
}
