// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.UI.Helpers;

internal static class ProtocolFallbackPreparedIconPolicy
{
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

        if (preparedIcon.Kind is IconPathConverter.PreparedIconKind.BitmapUri or IconPathConverter.PreparedIconKind.SvgUri
            && preparedIcon.Uri?.IsFile == true)
        {
            return File.Exists(preparedIcon.Uri.LocalPath);
        }

        return true;
    }
}
