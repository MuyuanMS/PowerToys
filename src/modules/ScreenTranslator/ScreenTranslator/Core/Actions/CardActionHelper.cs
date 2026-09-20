// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net.Mail;

namespace ScreenTranslator.Core.Actions;

public static class CardActionHelper
{
    public static Uri CreateSearchUri(string text)
    {
        return new Uri($"https://www.bing.com/search?q={Uri.EscapeDataString(text.Trim())}");
    }

    public static Uri CreateBingTranslatorUri(string text, string targetLanguage)
    {
        string target = string.IsNullOrWhiteSpace(targetLanguage) ? "en" : targetLanguage.Trim();
        return new Uri(
            $"https://www.bing.com/translator?from=auto-detect&to={Uri.EscapeDataString(target)}&text={Uri.EscapeDataString(text.Trim())}");
    }

    public static bool TryGetWebUri(string text, out Uri? uri)
    {
        uri = null;
        string candidate = text.Trim();
        if (candidate.Length == 0 || candidate.Contains(' '))
        {
            return false;
        }

        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !parsed.Host.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool TryGetEmailAddress(string text, out string? emailAddress)
    {
        emailAddress = null;
        string candidate = text.Trim();
        if (!MailAddress.TryCreate(candidate, out MailAddress? parsed) ||
            !string.Equals(parsed.Address, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        emailAddress = parsed.Address;
        return true;
    }
}
