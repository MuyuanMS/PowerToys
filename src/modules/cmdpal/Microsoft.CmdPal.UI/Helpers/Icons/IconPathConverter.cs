// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace Microsoft.CmdPal.UI.Helpers;

internal static partial class IconPathConverter
{
    private const string InvalidGlyph = "\u25CC";
    private const int DefaultBinaryIconSize = 256;

    public static PreparedIcon Prepare(string iconPath, string? fontFamily, int targetSize)
    {
        if (string.IsNullOrEmpty(iconPath))
        {
            return PreparedIcon.Empty();
        }

        if (IconPathParser.TryParseBinaryIconReference(iconPath, out var binaryIcon))
        {
            var bitmap = ExtractBinaryIcon(binaryIcon, targetSize >= 0 ? targetSize : DefaultBinaryIconSize);
            return PreparedIcon.FromBinary(bitmap);
        }

        // Font glyphs start outside ASCII, while every supported URI starts inside it.
        // Avoid using exception-based URI probing for the common Fluent glyph case.
        if (iconPath[0] < 128 && Uri.TryCreate(iconPath, UriKind.Absolute, out var uri))
        {
            var isSvg = Path.GetExtension(uri.AbsolutePath).Equals(".svg", StringComparison.OrdinalIgnoreCase);
            return PreparedIcon.FromUri(uri, isSvg, targetSize);
        }

        var glyphKind = FontIconGlyphClassifier.Classify(iconPath);
        var glyph = glyphKind == FontIconGlyphKind.Invalid ? InvalidGlyph : iconPath;
        var family = FontIconGlyphClassifier.GetFontFamily(glyphKind, fontFamily);
        return PreparedIcon.FromGlyph(glyph, family, targetSize > 0 ? targetSize : 8);
    }

    public static async Task<IconSource> CreateIconSourceAsync(PreparedIcon icon)
    {
        try
        {
            switch (icon.Kind)
            {
                case PreparedIconKind.BitmapUri:
                    var bitmap = new BitmapImage
                    {
                        DecodePixelWidth = icon.TargetSize > 0 ? icon.TargetSize : 0,
                        UriSource = icon.Uri!,
                    };
                    return new ImageIconSource { ImageSource = bitmap };

                case PreparedIconKind.SvgUri:
                    var svg = new SvgImageSource(icon.Uri!);
                    if (icon.TargetSize > 0)
                    {
                        svg.RasterizePixelWidth = icon.TargetSize;
                    }

                    return new ImageIconSource { ImageSource = svg };

                case PreparedIconKind.Glyph:
                    return new FontIconSource
                    {
                        FontFamily = new FontFamily(icon.FontFamily!),
                        FontSize = icon.TargetSize,
                        Glyph = icon.Glyph!,
                    };

                case PreparedIconKind.Binary:
                    var softwareBitmap = icon.TakeSoftwareBitmap();
                    if (softwareBitmap is null)
                    {
                        return new ImageIconSource();
                    }

                    var ownershipTransferred = false;
                    try
                    {
                        var bitmapSource = new SoftwareBitmapSource();
                        try
                        {
                            await bitmapSource.SetBitmapAsync(softwareBitmap);

                            var iconSource = new ImageIconSource { ImageSource = bitmapSource };

                            // SetBitmapAsync can finish before WinUI's AsyncCopyToSurfaceTask.
                            // Once XAML accepts the bitmap, explicitly closing either object can
                            // fail-fast that later copy with RO_E_CLOSED. Release both through
                            // their normal WinRT reference lifetimes instead.
                            ownershipTransferred = true;
                            return iconSource;
                        }
                        catch
                        {
                            // The source has not escaped to a caller or visual tree.
                            bitmapSource.Dispose();
                            throw;
                        }
                    }
                    finally
                    {
                        if (!ownershipTransferred)
                        {
                            softwareBitmap.Dispose();
                        }
                    }

                default:
                    return CreateEmptyIconSource();
            }
        }
        catch
        {
            return icon.Kind == PreparedIconKind.Binary
                ? new ImageIconSource()
                : CreateEmptyIconSource();
        }
    }

    // Keep the empty value non-null. A virtualized ListView can crash when a
    // data-bound IconSourceElement alternates between null and non-null sources;
    // a BitmapIconSource with a null URI remains visually empty without crossing
    // that unstable boundary.
    private static BitmapIconSource CreateEmptyIconSource() => new() { UriSource = null };

    private static SoftwareBitmap? ExtractBinaryIcon(BinaryIconReference iconReference, int targetSize)
    {
        nint iconHandle = 0;
        try
        {
            _ = NativeMethods.SHDefExtractIcon(
                iconReference.Path,
                iconReference.Index,
                0,
                out iconHandle,
                0,
                (uint)targetSize);
            if (iconHandle == 0)
            {
                return null;
            }

            return CreateSoftwareBitmapFromIcon(iconHandle, targetSize);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (iconHandle != 0)
            {
                _ = NativeMethods.DestroyIcon(iconHandle);
            }
        }
    }

    private static SoftwareBitmap? CreateSoftwareBitmapFromIcon(nint iconHandle, int targetSize)
    {
        const uint DibRgbColors = 0;
        const uint DiNormal = 0x0003;
        const int SystemIconWidth = 11;

        var bitmapSize = targetSize > 0 ? targetSize : NativeMethods.GetSystemMetrics(SystemIconWidth);
        if (bitmapSize <= 0)
        {
            return null;
        }

        var screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0)
        {
            return null;
        }

        nint iconDc = 0;
        nint blackBitmap = 0;
        nint whiteBitmap = 0;
        nint originalBitmap = 0;
        byte[]? blackPixels = null;
        byte[]? whitePixels = null;
        try
        {
            iconDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (iconDc == 0)
            {
                return null;
            }

            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = bitmapSize,
                    Height = -bitmapSize,
                    Planes = 1,
                    BitCount = 32,
                },
            };
            var byteCount = checked(bitmapSize * bitmapSize * 4);

            blackBitmap = NativeMethods.CreateDIBSection(
                screenDc,
                in bitmapInfo,
                DibRgbColors,
                out var blackBits,
                0,
                0);
            if (blackBitmap == 0 || blackBits == 0)
            {
                return null;
            }

            originalBitmap = NativeMethods.SelectObject(iconDc, blackBitmap);
            blackPixels = ArrayPool<byte>.Shared.Rent(byteCount);
            Array.Clear(blackPixels, 0, byteCount);
            Marshal.Copy(blackPixels, 0, blackBits, byteCount);
            if (!NativeMethods.DrawIconEx(iconDc, 0, 0, iconHandle, bitmapSize, bitmapSize, 0, 0, DiNormal))
            {
                return null;
            }

            Marshal.Copy(blackBits, blackPixels, 0, byteCount);
            var hasAlpha = false;
            for (var offset = 3; offset < byteCount; offset += 4)
            {
                if (blackPixels[offset] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (hasAlpha)
            {
                for (var offset = 0; offset < byteCount; offset += 4)
                {
                    var alpha = blackPixels[offset + 3];
                    if (alpha == 0)
                    {
                        blackPixels[offset] = 0;
                        blackPixels[offset + 1] = 0;
                        blackPixels[offset + 2] = 0;
                    }
                    else if (blackPixels[offset] > alpha
                        || blackPixels[offset + 1] > alpha
                        || blackPixels[offset + 2] > alpha)
                    {
                        blackPixels[offset] = Premultiply(blackPixels[offset], alpha);
                        blackPixels[offset + 1] = Premultiply(blackPixels[offset + 1], alpha);
                        blackPixels[offset + 2] = Premultiply(blackPixels[offset + 2], alpha);
                    }
                }
            }
            else
            {
                whiteBitmap = NativeMethods.CreateDIBSection(
                    screenDc,
                    in bitmapInfo,
                    DibRgbColors,
                    out var whiteBits,
                    0,
                    0);
                if (whiteBitmap == 0 || whiteBits == 0)
                {
                    return null;
                }

                _ = NativeMethods.SelectObject(iconDc, whiteBitmap);
                whitePixels = ArrayPool<byte>.Shared.Rent(byteCount);
                Array.Fill(whitePixels, byte.MaxValue, 0, byteCount);
                Marshal.Copy(whitePixels, 0, whiteBits, byteCount);
                if (!NativeMethods.DrawIconEx(iconDc, 0, 0, iconHandle, bitmapSize, bitmapSize, 0, 0, DiNormal))
                {
                    return null;
                }

                Marshal.Copy(whiteBits, whitePixels, 0, byteCount);
                for (var offset = 0; offset < byteCount; offset += 4)
                {
                    var backgroundDifference =
                        whitePixels[offset] - blackPixels[offset]
                        + whitePixels[offset + 1] - blackPixels[offset + 1]
                        + whitePixels[offset + 2] - blackPixels[offset + 2];
                    if (backgroundDifference < 384)
                    {
                        blackPixels[offset + 3] = byte.MaxValue;
                    }
                    else
                    {
                        blackPixels[offset] = 0;
                        blackPixels[offset + 1] = 0;
                        blackPixels[offset + 2] = 0;
                        blackPixels[offset + 3] = 0;
                    }
                }
            }

            var softwareBitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8,
                bitmapSize,
                bitmapSize,
                BitmapAlphaMode.Premultiplied);
            softwareBitmap.CopyFromBuffer(blackPixels.AsBuffer(0, byteCount));
            return softwareBitmap;
        }
        finally
        {
            if (whitePixels is not null)
            {
                ArrayPool<byte>.Shared.Return(whitePixels);
            }

            if (blackPixels is not null)
            {
                ArrayPool<byte>.Shared.Return(blackPixels);
            }

            if (iconDc != 0 && originalBitmap != 0)
            {
                _ = NativeMethods.SelectObject(iconDc, originalBitmap);
            }

            if (whiteBitmap != 0)
            {
                _ = NativeMethods.DeleteObject(whiteBitmap);
            }

            if (blackBitmap != 0)
            {
                _ = NativeMethods.DeleteObject(blackBitmap);
            }

            if (iconDc != 0)
            {
                _ = NativeMethods.DeleteDC(iconDc);
            }

            _ = NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    private static byte Premultiply(byte color, byte alpha) =>
        (byte)(((color * alpha) + 127) / byte.MaxValue);

    internal sealed partial class PreparedIcon : IDisposable
    {
        private SoftwareBitmap? _softwareBitmap;

        private PreparedIcon(
            PreparedIconKind kind,
            Uri? uri = null,
            string? glyph = null,
            string? fontFamily = null,
            SoftwareBitmap? softwareBitmap = null,
            int targetSize = 0)
        {
            Kind = kind;
            Uri = uri;
            Glyph = glyph;
            FontFamily = fontFamily;
            _softwareBitmap = softwareBitmap;
            TargetSize = targetSize;
        }

        public PreparedIconKind Kind { get; }

        public Uri? Uri { get; }

        public string? Glyph { get; }

        public string? FontFamily { get; }

        public SoftwareBitmap? SoftwareBitmap => _softwareBitmap;

        public int TargetSize { get; }

        public static PreparedIcon Empty() => new(PreparedIconKind.Empty);

        public static PreparedIcon FromUri(Uri uri, bool isSvg, int targetSize) =>
            new(isSvg ? PreparedIconKind.SvgUri : PreparedIconKind.BitmapUri, uri: uri, targetSize: targetSize);

        public static PreparedIcon FromGlyph(string glyph, string fontFamily, int targetSize) =>
            new(PreparedIconKind.Glyph, glyph: glyph, fontFamily: fontFamily, targetSize: targetSize);

        public static PreparedIcon FromBinary(SoftwareBitmap? bitmap) =>
            new(PreparedIconKind.Binary, softwareBitmap: bitmap);

        // Asynchronous materialization takes ownership before the PreparedIcon is
        // disposed. On success, XAML owns the bitmap's remaining lifetime.
        public SoftwareBitmap? TakeSoftwareBitmap() =>
            Interlocked.Exchange(ref _softwareBitmap, null);

        public void Dispose()
        {
            Interlocked.Exchange(ref _softwareBitmap, null)?.Dispose();
        }
    }

    internal enum PreparedIconKind
    {
        Empty,
        BitmapUri,
        SvgUri,
        Glyph,
        Binary,
    }

    private static partial class NativeMethods
    {
        [LibraryImport("shell32.dll", EntryPoint = "SHDefExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int SHDefExtractIcon(
            string iconFile,
            int iconIndex,
            uint flags,
            out nint largeIcon,
            nint smallIcon,
            uint iconSize);

        [LibraryImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DestroyIcon(nint icon);

        [LibraryImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial nint GetDC(nint window);

        [LibraryImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int ReleaseDC(nint window, nint deviceContext);

        [LibraryImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int GetSystemMetrics(int index);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial bool DrawIconEx(
            nint deviceContext,
            int x,
            int y,
            nint icon,
            int width,
            int height,
            uint animationStep,
            nint flickerFreeBrush,
            uint flags);

        [LibraryImport("gdi32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial nint CreateCompatibleDC(nint deviceContext);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial bool DeleteDC(nint deviceContext);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial bool DeleteObject(nint graphicsObject);

        [LibraryImport("gdi32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial nint SelectObject(nint deviceContext, nint graphicsObject);

        [LibraryImport("gdi32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial nint CreateDIBSection(
            nint deviceContext,
            in BitmapInfo bitmapInfo,
            uint usage,
            out nint bits,
            nint section,
            uint offset);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }
}
