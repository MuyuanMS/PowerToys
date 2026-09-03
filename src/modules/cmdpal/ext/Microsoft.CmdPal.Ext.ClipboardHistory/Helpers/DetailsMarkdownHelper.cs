// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.CmdPal.Ext.ClipboardHistory.Helpers;

internal static class DetailsMarkdownHelper
{
    public static string BuildTextBody(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var fence = new string('`', Math.Max(3, GetLongestBacktickRun(text) + 1));

        return $"{fence}text\n{text}\n{fence}";
    }

    public static string BuildImageBody(string? imagePath, string altText)
        => string.IsNullOrEmpty(imagePath)
            ? string.Empty
            : $"![{altText}]({new Uri(imagePath).AbsoluteUri}?--x-cmdpal-fit=fit)";

    private static int GetLongestBacktickRun(string text)
    {
        var longestRun = 0;
        var currentRun = 0;

        foreach (var character in text)
        {
            if (character == '`')
            {
                currentRun++;
                longestRun = Math.Max(longestRun, currentRun);
            }
            else
            {
                currentRun = 0;
            }
        }

        return longestRun;
    }
}
