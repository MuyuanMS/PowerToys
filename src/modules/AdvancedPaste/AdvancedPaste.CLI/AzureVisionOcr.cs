// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdvancedPaste.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Security.Credentials;
using Windows.Storage;

namespace AdvancedPaste.Cli;

internal static class AzureVisionOcr
{
    internal const string CredentialResource = "PowerToys.AdvancedPaste.Demo.AzureVisionOCR";
    private const int MaximumImageBytes = 16 * 1024 * 1024;
    private const int MaximumResponseBytes = 1024 * 1024;

    internal static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/" ||
            (!uri.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase) &&
             !uri.Host.EndsWith(".api.cognitive.microsoft.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Enter the HTTPS endpoint from your Azure Computer Vision resource's Keys and Endpoint page, without an API path.");
        }

        return uri;
    }

    internal static void SaveProfile(string endpoint, string key)
    {
        var uri = ValidateEndpoint(endpoint);
        ValidateKey(key);
        var vault = new PasswordVault();
        var previous = FindProfile(vault);
        vault.Add(new PasswordCredential(CredentialResource, uri.AbsoluteUri, key));
        if (previous is not null && previous.UserName != uri.AbsoluteUri)
        {
            vault.Remove(previous);
        }
    }

    internal static void RemoveProfile()
    {
        var vault = new PasswordVault();
        var profile = FindProfile(vault);
        if (profile is not null)
        {
            vault.Remove(profile);
        }
    }

    private static PasswordCredential? FindProfile(PasswordVault vault)
    {
        try
        {
            var profiles = vault.FindAllByResource(CredentialResource);
            return profiles.Count switch
            {
                0 => null,
                1 => profiles[0],
                _ => throw new CliActionUnavailableException("Multiple Azure OCR profiles exist. Reconfigure the demo OCR profile."),
            };
        }
        catch (COMException exception) when (exception.HResult == unchecked((int)0x80070490))
        {
            return null;
        }
    }

    internal static async Task<DataPackage?> TryExtractAsync(DataPackageView input, CancellationToken cancellationToken)
    {
        var profile = FindProfile(new PasswordVault());
        if (profile is null)
        {
            return null;
        }

        if (!AdvancedPastePolicy.IsOnlineAIAllowed)
        {
            throw new CliActionUnavailableException("Online AI is disabled by policy. Azure OCR was not called.");
        }

        var endpoint = ValidateEndpoint(profile.UserName);
        profile.RetrievePassword();
        var bytes = await ReadImageAsync(input, cancellationToken);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        var text = await ReadAsync(endpoint, profile.Password, bytes, client, cancellationToken);
        var result = new DataPackage();
        result.SetText(text);
        return result;
    }

    internal static async Task<string> ReadAsync(Uri endpoint, string key, byte[] image, HttpClient client, CancellationToken cancellationToken)
    {
        endpoint = ValidateEndpoint(endpoint.AbsoluteUri);
        ValidateKey(key);
        if (image.Length is 0 or > MaximumImageBytes)
        {
            throw new CliActionUnavailableException("Azure OCR accepts one image up to 16 MB.");
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(25));
        var uri = new Uri(endpoint, "vision/v3.2/read/analyze");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        request.Content = new ByteArrayContent(image);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        try
        {
            budget.Token.ThrowIfCancellationRequested();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            EnsureSuccess(response);
            if (response.StatusCode != System.Net.HttpStatusCode.Accepted ||
                !response.Headers.TryGetValues("Operation-Location", out var locations))
            {
                throw new CliActionUnavailableException("Azure did not return a Read operation. Check that the resource supports Computer Vision Read 3.2.");
            }

            var operation = ValidateOperationLocation(endpoint, locations.Single());
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), budget.Token);
                using var poll = new HttpRequestMessage(HttpMethod.Get, operation);
                poll.Headers.Add("Ocp-Apim-Subscription-Key", key);
                using var result = await client.SendAsync(poll, HttpCompletionOption.ResponseHeadersRead, budget.Token);
                EnsureSuccess(result);
                using var stream = await result.Content.ReadAsStreamAsync(budget.Token);
                var data = await ReadBoundedAsync(stream, MaximumResponseBytes, budget.Token);
                using var json = JsonDocument.Parse(data);
                var status = json.RootElement.TryGetProperty("status", out var value) ? value.GetString() : null;
                if (status == "succeeded")
                {
                    return ParseText(json.RootElement);
                }

                if (status is not ("notStarted" or "running"))
                {
                    throw new CliActionUnavailableException("Azure Read failed or returned an unexpected status. No retry or offline fallback was used.");
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliActionUnavailableException("Azure OCR timed out. The image may already have been submitted; no retry or offline fallback was used.");
        }

        throw new CliActionUnavailableException("Azure Read is still processing the submitted image. No retry or offline fallback was used.");
    }

    internal static Uri ValidateOperationLocation(Uri endpoint, string location)
    {
        const string prefix = "/vision/v3.2/read/analyzeResults/";
        if (!Uri.TryCreate(location, UriKind.Absolute, out var operation) ||
            operation.Scheme != Uri.UriSchemeHttps || operation.Port != 443 ||
            !string.Equals(operation.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(operation.UserInfo) || !string.IsNullOrEmpty(operation.Query) ||
            !string.IsNullOrEmpty(operation.Fragment) ||
            !operation.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(operation.AbsolutePath[prefix.Length..], "D", out _))
        {
            throw new CliActionUnavailableException("Azure returned an unexpected operation URL. The key was not forwarded.");
        }

        return operation;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new CliActionUnavailableException(
                $"Azure OCR returned HTTP {(int)response.StatusCode}. Check the endpoint, key, resource access and quota. No offline fallback was used.");
        }
    }

    internal static string ParseText(JsonElement root)
    {
        if (!root.TryGetProperty("analyzeResult", out var read) ||
            !read.TryGetProperty("readResults", out var pages) || pages.ValueKind != JsonValueKind.Array)
        {
            throw new CliActionUnavailableException("Azure did not return Read OCR results. Check that the resource supports Computer Vision Read 3.2.");
        }

        var text = string.Join(
            Environment.NewLine,
            pages.EnumerateArray().SelectMany(page => page.GetProperty("lines").EnumerateArray())
                .Select(line => line.GetProperty("text").GetString()).Where(line => !string.IsNullOrWhiteSpace(line)));
        return !string.IsNullOrWhiteSpace(text) ? text :
            throw new CliActionUnavailableException("Azure OCR found no readable text in this image.");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 512 || key.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Enter Key 1 or Key 2 from your Computer Vision resource. Do not use the Azure subscription ID.");
        }
    }

    private static async Task<byte[]> ReadImageAsync(DataPackageView input, CancellationToken cancellationToken)
    {
        if (input.Contains(StandardDataFormats.Bitmap))
        {
            using var bitmap = await (await input.GetBitmapAsync()).OpenReadAsync();
            using var stream = bitmap.AsStreamForRead();
            return await ReadBoundedAsync(stream, MaximumImageBytes, cancellationToken);
        }

        var items = input.Contains(StandardDataFormats.StorageItems) ? await input.GetStorageItemsAsync() : [];
        if (items.Count != 1 || items[0] is not StorageFile file ||
            !new[] { ".png", ".jpg", ".jpeg" }.Contains(file.FileType, StringComparer.OrdinalIgnoreCase))
        {
            throw new CliActionUnavailableException("Azure OCR requires one PNG or JPEG image.");
        }

        using var source = await file.OpenReadAsync();
        using var fileStream = source.AsStreamForRead();
        return await ReadBoundedAsync(fileStream, MaximumImageBytes, cancellationToken);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, int maximum, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > maximum)
            {
                throw new CliActionUnavailableException("Azure OCR input or response exceeded its size limit.");
            }

            buffer.Write(chunk, 0, count);
        }

        return buffer.ToArray();
    }
}
