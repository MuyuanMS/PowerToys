// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.UI.Helpers;

internal static class ProtocolFallbackPreparedIconPolicy
{
    private const string InvalidGlyph = "\u25CC";

    private static readonly HashSet<string> DecodableUriSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeFile,
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "ms-appx",
        "ms-appdata",
    };

    public static bool ShouldUse(IconPathConverter.PreparedIcon preparedIcon)
    {
        if (preparedIcon.Kind == IconPathConverter.PreparedIconKind.Empty)
        {
            return false;
        }

        if (preparedIcon.Kind == IconPathConverter.PreparedIconKind.Binary)
        {
            return preparedIcon.SoftwareBitmap is not null;
        }

        if (preparedIcon.Kind == IconPathConverter.PreparedIconKind.Glyph)
        {
            return !string.Equals(preparedIcon.Glyph, InvalidGlyph, StringComparison.Ordinal);
        }

        if (preparedIcon.Kind is IconPathConverter.PreparedIconKind.BitmapUri or IconPathConverter.PreparedIconKind.SvgUri
            && preparedIcon.Uri is { } uri)
        {
            return DecodableUriSchemes.Contains(uri.Scheme)
                && (!uri.IsFile || File.Exists(uri.LocalPath));
        }

        return true;
    }
}
