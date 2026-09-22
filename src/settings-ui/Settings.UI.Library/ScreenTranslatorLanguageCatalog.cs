// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ManagedCommon;

namespace Microsoft.PowerToys.Settings.UI.Library;

public static class ScreenTranslatorLanguageCatalog
{
    public static IReadOnlyList<ScreenTranslatorLanguageOption> CreateSystemFallback()
    {
        return CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures)
            .Where(culture => !string.IsNullOrWhiteSpace(culture.Name))
            .GroupBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(culture => new ScreenTranslatorLanguageOption(
                culture.Name,
                $"{culture.NativeName} ({culture.Name})"))
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task<IReadOnlyList<ScreenTranslatorLanguageOption>> GetTranslationLanguagesAsync(
        string providerId,
        string? azureEndpoint,
        string? libreTranslateEndpoint,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        bool disposeClient = httpClient is null;
        httpClient ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            IReadOnlyList<ScreenTranslatorLanguageOption> languages =
                string.Equals(providerId, "AzureTranslator", StringComparison.OrdinalIgnoreCase)
                    ? await GetAzureLanguagesAsync(httpClient, azureEndpoint, cancellationToken).ConfigureAwait(false)
                    : string.Equals(providerId, "LibreTranslate", StringComparison.OrdinalIgnoreCase)
                        ? await GetLibreTranslateLanguagesAsync(httpClient, libreTranslateEndpoint, cancellationToken).ConfigureAwait(false)
                        : Array.Empty<ScreenTranslatorLanguageOption>();

            return languages.Count > 0 ? languages : CreateSystemFallback();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning($"Screen Translator could not load the provider language catalog: {ex.Message}");
            return CreateSystemFallback();
        }
        finally
        {
            if (disposeClient)
            {
                httpClient.Dispose();
            }
        }
    }

    public static IReadOnlyList<ScreenTranslatorLanguageOption> CreateSourceOptions(
        IEnumerable<ScreenTranslatorLanguageOption> translationLanguages,
        bool cloudOcrEnabled,
        IEnumerable<string> installedOcrLanguageTags)
    {
        HashSet<string> installedTags = new(
            installedOcrLanguageTags.Where(tag => !string.IsNullOrWhiteSpace(tag)),
            StringComparer.OrdinalIgnoreCase);
        List<ScreenTranslatorLanguageOption> sourceOptions =
        [
            new("auto", "Auto-detect / System"),
        ];

        sourceOptions.AddRange(translationLanguages.Select(language =>
        {
            bool isEnabled = cloudOcrEnabled ||
                installedTags.Any(installed => AreEquivalentLanguageTags(installed, language.Code));
            string displayName = isEnabled
                ? language.DisplayName
                : $"{language.DisplayName} - local OCR not installed";
            return new ScreenTranslatorLanguageOption(language.Code, displayName, isEnabled);
        }));
        return sourceOptions;
    }

    public static bool AreEquivalentLanguageTags(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string leftPrimary = left.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        string rightPrimary = right.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        if (!string.Equals(leftPrimary, rightPrimary, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(leftPrimary, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? leftScript = GetChineseScript(left);
        string? rightScript = GetChineseScript(right);
        return leftScript is null ||
            rightScript is null ||
            string.Equals(leftScript, rightScript, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<ScreenTranslatorLanguageOption>> GetAzureLanguagesAsync(
        HttpClient httpClient,
        string? endpoint,
        CancellationToken cancellationToken)
    {
        string baseEndpoint = string.IsNullOrWhiteSpace(endpoint)
            ? "https://api.cognitive.microsofttranslator.com"
            : endpoint.Trim().TrimEnd('/');
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{baseEndpoint}/languages?api-version=3.0&scope=translation",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("translation", out JsonElement translations))
        {
            return Array.Empty<ScreenTranslatorLanguageOption>();
        }

        List<ScreenTranslatorLanguageOption> languages = [];
        foreach (JsonProperty translation in translations.EnumerateObject())
        {
            string name = translation.Value.TryGetProperty("nativeName", out JsonElement nativeName)
                ? nativeName.GetString() ?? translation.Name
                : translation.Value.TryGetProperty("name", out JsonElement displayName)
                    ? displayName.GetString() ?? translation.Name
                    : translation.Name;
            languages.Add(new ScreenTranslatorLanguageOption(
                translation.Name,
                $"{name} ({translation.Name})"));
        }

        return languages
            .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static async Task<IReadOnlyList<ScreenTranslatorLanguageOption>> GetLibreTranslateLanguagesAsync(
        HttpClient httpClient,
        string? endpoint,
        CancellationToken cancellationToken)
    {
        string baseEndpoint = string.IsNullOrWhiteSpace(endpoint)
            ? "http://localhost:5000"
            : endpoint.Trim().TrimEnd('/');
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{baseEndpoint}/languages",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        Dictionary<string, string> languages = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement language in document.RootElement.EnumerateArray())
        {
            if (!language.TryGetProperty("code", out JsonElement codeElement) ||
                string.IsNullOrWhiteSpace(codeElement.GetString()))
            {
                continue;
            }

            string code = codeElement.GetString()!;
            string name = language.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? code
                : code;
            languages[code] = name;

            if (language.TryGetProperty("targets", out JsonElement targets))
            {
                foreach (JsonElement target in targets.EnumerateArray())
                {
                    string? targetCode = target.GetString();
                    if (!string.IsNullOrWhiteSpace(targetCode))
                    {
                        languages.TryAdd(targetCode, GetCultureDisplayName(targetCode));
                    }
                }
            }
        }

        return languages
            .Select(language => new ScreenTranslatorLanguageOption(
                language.Key,
                $"{language.Value} ({language.Key})"))
            .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string GetCultureDisplayName(string languageTag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageTag).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return languageTag;
        }
    }

    private static string? GetChineseScript(string languageTag)
    {
        string[] subtags = languageTag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (subtags.Any(subtag =>
            string.Equals(subtag, "Hans", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subtag, "CN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subtag, "SG", StringComparison.OrdinalIgnoreCase)))
        {
            return "Hans";
        }

        if (subtags.Any(subtag =>
            string.Equals(subtag, "Hant", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subtag, "TW", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subtag, "HK", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subtag, "MO", StringComparison.OrdinalIgnoreCase)))
        {
            return "Hant";
        }

        return null;
    }
}
