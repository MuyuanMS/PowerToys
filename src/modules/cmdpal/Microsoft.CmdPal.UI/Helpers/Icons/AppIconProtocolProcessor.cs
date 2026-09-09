// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.UI.Xaml;
using Windows.Storage.Streams;

namespace Microsoft.CmdPal.UI.Helpers;

internal sealed class AppIconProtocolProcessor : IIconProtocolProcessor
{
    private readonly Func<string, bool, Task<IRandomAccessStream?>> _getThumbnail;

    public static AppIconProtocolProcessor Instance { get; } = new();

    private AppIconProtocolProcessor()
        : this(ThumbnailHelper.GetThumbnail)
    {
    }

    internal AppIconProtocolProcessor(Func<string, bool, Task<IRandomAccessStream?>> getThumbnail)
    {
        _getThumbnail = getThumbnail;
    }

    public IconCachePartition CachePartition => IconCachePartition.Other;

    public ReadOnlySpan<string> ProtocolPrefixes => AppIconProtocol.ProtocolPrefixes;

    public ElementTheme GetCacheTheme(string value, ElementTheme theme) => ElementTheme.Default;

    public IconLoadInputKind ClassifyInput(string value) => IconLoadInputKind.SpecializedAppIcon;

    public bool TryPrepareSynchronously(
        string value,
        int targetSize,
        ElementTheme theme,
        out IconPathConverter.PreparedIcon preparedIcon)
    {
        preparedIcon = null!;
        return false;
    }

    public async ValueTask<IconProtocolProcessingResult> PrepareAsync(
        string value,
        string? fontFamily,
        int targetSize,
        ElementTheme theme)
    {
        if (!AppIconProtocol.TryParse(value, out var candidates, out var jumbo))
        {
            return IconProtocolProcessingResult.Empty();
        }

        foreach (var candidate in candidates)
        {
            var preparedIcon = IconPathConverter.PrepareFirstAvailable([candidate], fontFamily, targetSize, theme);
            if (ShouldPreferPreparedIcon(preparedIcon))
            {
                return IconProtocolProcessingResult.FromPreparedIcon(preparedIcon);
            }

            if (!ShouldSkipThumbnailLookup(candidate))
            {
                try
                {
                    if (await _getThumbnail(candidate, jumbo).ConfigureAwait(false) is { } stream)
                    {
                        preparedIcon.Dispose();
                        return IconProtocolProcessingResult.FromBitmapStream(stream);
                    }
                }
                catch
                {
                    // Continue with ordinary conversion for this same candidate.
                }
            }

            if (preparedIcon.Kind != IconPathConverter.PreparedIconKind.Empty)
            {
                return IconProtocolProcessingResult.FromPreparedIcon(preparedIcon);
            }

            preparedIcon.Dispose();
        }

        return IconProtocolProcessingResult.Empty();
    }

    private static bool ShouldSkipThumbnailLookup(string candidate)
    {
        if (candidate.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        return (IconPathParser.TryParseBinaryIconReference(candidate, out _) && candidate.Contains(',', StringComparison.Ordinal))
            || (Path.IsPathRooted(candidate) && !File.Exists(candidate));
    }

    private static bool ShouldPreferPreparedIcon(IconPathConverter.PreparedIcon preparedIcon) =>
        preparedIcon.Kind is IconPathConverter.PreparedIconKind.BitmapUri
            or IconPathConverter.PreparedIconKind.SvgUri
            or IconPathConverter.PreparedIconKind.Glyph;
}
