// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.PowerToys.UITest.Next;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CropAndLock.UITests
{
    internal sealed class CropImage
    {
        private readonly byte[] pixels;
        private readonly int stride;

        private CropImage(Bitmap bitmap)
        {
            Size = bitmap.Size;
            var data = bitmap.LockBits(new Rectangle(Point.Empty, Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = data.Stride;
                pixels = new byte[stride * Size.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        internal Size Size { get; }

        internal readonly record struct Comparison(double AllPixels, double ContentPixels, int ContentCount)
        {
            internal bool Matches => AllPixels >= 0.98 && ContentPixels >= 0.90 && ContentCount >= 100;
        }

        internal static CropImage Capture(IntPtr window, Rectangle clientRegion, string path)
        {
            var client = NativeMethods.ClientBounds(window);
            var region = clientRegion;
            region.Offset(client.Location);
            var (left, top, right, bottom) = WindowHelper.GetVisibleBounds(window);
            var frame = Rectangle.FromLTRB(left, top, right, bottom);
            Assert.IsTrue(frame.Contains(region), $"Capture region {region} is outside the DWM frame {frame}.");

            // The shared helper captures composed desktop pixels, not PrintWindow/UIA. Both UWP
            // content and DWM thumbnails would otherwise be absent from an apparently valid image.
            var windowPath = Path.ChangeExtension(path, ".window.png");
            WindowHelper.CaptureVisibleWindow(window, windowPath);
            using var full = new Bitmap(windowPath);
            region.Offset(-frame.Left, -frame.Top);
            using var cropped = full.Clone(region, PixelFormat.Format32bppArgb);
            cropped.Save(path, ImageFormat.Png);
            return new CropImage(cropped);
        }

        internal static Comparison Compare(CropImage expected, CropImage actual)
        {
            if (expected.Size != actual.Size)
            {
                return default;
            }

            // Weight the foreground separately: a blank input must not pass simply because
            // its background occupies more than 98% of the crop. Ignore the outer focus border.
            var colors = new Dictionary<int, int>();
            for (var y = 4; y < expected.Size.Height - 4; y++)
            {
                for (var x = 4; x < expected.Size.Width - 4; x++)
                {
                    var index = (y * expected.stride) + (x * 4);
                    var color = (expected.pixels[index] >> 4) | ((expected.pixels[index + 1] >> 4) << 4) | ((expected.pixels[index + 2] >> 4) << 8);
                    colors[color] = colors.GetValueOrDefault(color) + 1;
                }
            }

            if (colors.Count == 0)
            {
                return default;
            }

            var background = colors.MaxBy(entry => entry.Value).Key;
            var total = 0;
            var matched = 0;
            var content = 0;
            var contentMatched = 0;
            for (var y = 4; y < expected.Size.Height - 4; y++)
            {
                for (var x = 4; x < expected.Size.Width - 4; x++)
                {
                    var expectedIndex = (y * expected.stride) + (x * 4);
                    var actualIndex = (y * actual.stride) + (x * 4);
                    var matches = true;
                    var isContent = false;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        matches &= Math.Abs(expected.pixels[expectedIndex + channel] - actual.pixels[actualIndex + channel]) <= 30;
                        var backgroundChannel = ((background >> (channel * 4)) & 15) * 16;
                        isContent |= Math.Abs(expected.pixels[expectedIndex + channel] - backgroundChannel) > 40;
                    }

                    total++;
                    matched += matches ? 1 : 0;
                    content += isContent ? 1 : 0;
                    contentMatched += matches && isContent ? 1 : 0;
                }
            }

            return new Comparison((double)matched / total, content == 0 ? 0 : (double)contentMatched / content, content);
        }
    }
}
