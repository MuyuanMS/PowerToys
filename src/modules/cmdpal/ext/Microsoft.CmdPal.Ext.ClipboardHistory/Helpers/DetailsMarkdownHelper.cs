// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text.RegularExpressions;
using ManagedCommon;
using Windows.Storage.Streams;

namespace Microsoft.CmdPal.Ext.ClipboardHistory.Helpers;

internal static partial class DetailsMarkdownHelper
{
    public static string EscapeMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return MarkdownSpecialCharacters().Replace(text, "\\$1");
    }

    [GeneratedRegex(@"([\\`*_{}\[\]()#+\-.!>|~])")]
    private static partial Regex MarkdownSpecialCharacters();

    public static string BuildImageBody(RandomAccessStreamReference? imageData, string altText)
    {
        if (imageData is null)
        {
            return string.Empty;
        }

        try
        {
            using var stream = imageData.OpenReadAsync().AsTask().GetAwaiter().GetResult();
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            reader.LoadAsync((uint)stream.Size).AsTask().GetAwaiter().GetResult();
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            return BuildImageBodyFromBytes(bytes, altText);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Failed to build image preview for details:" + ex);
            return string.Empty;
        }
    }

    public static string BuildImageBodyFromBytes(byte[] imageBytes, string altText)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var base64 = Convert.ToBase64String(imageBytes);
        return $"![{altText}](data:image/png;base64,{base64}#--x-cmdpal-fit=fit)";
    }
}
