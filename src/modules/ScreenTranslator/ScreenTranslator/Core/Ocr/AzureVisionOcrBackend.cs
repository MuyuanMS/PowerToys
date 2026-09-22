// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenTranslator.Core.Layout;
using ScreenTranslator.Core.Translation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ScreenTranslator.Core.Ocr;

/// <summary>
/// Azure AI Vision Image Analysis Read OCR backend.
/// </summary>
public sealed class AzureVisionOcrBackend : IOcrBackend, IDisposable
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly bool _cloudConsentEnabled;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public AzureVisionOcrBackend(
        string endpoint,
        string apiKey,
        bool cloudConsentEnabled,
        HttpClient? customHttpClient = null)
    {
        _endpoint = endpoint?.TrimEnd('/') ?? string.Empty;
        _apiKey = apiKey?.Trim() ?? string.Empty;
        _cloudConsentEnabled = cloudConsentEnabled;
        _httpClient = customHttpClient ?? new HttpClient();
        _ownsHttpClient = customHttpClient is null;
    }

    public string BackendName => "Azure AI Vision OCR (Cloud)";

    public bool IsAvailable =>
        _cloudConsentEnabled &&
        Uri.TryCreate(_endpoint, UriKind.Absolute, out Uri? endpoint) &&
        endpoint.Scheme is "https" or "http" &&
        !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IReadOnlyList<TranslationLine>> RecognizeTextAsync(
        SoftwareBitmap bitmap,
        PhysicalRect capturedRegionPhysical,
        string? sourceLanguageTag = null,
        CancellationToken cancellationToken = default)
    {
        if (!_cloudConsentEnabled)
        {
            throw new InvalidOperationException("Cloud OCR consent is required.");
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("Azure Vision OCR API key is not configured.");
        }

        if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out Uri? endpoint))
        {
            throw new InvalidOperationException("Azure Vision OCR endpoint is invalid.");
        }

        if (bitmap is null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return Array.Empty<TranslationLine>();
        }

        using InMemoryRandomAccessStream imageStream = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, imageStream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        imageStream.Seek(0);
        using StreamContent content = new(imageStream.AsStreamForRead());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        Uri requestUri = new(
            $"{endpoint}/computervision/imageanalysis:analyze?api-version=2024-02-01&features=read");
        using HttpRequestMessage request = new(HttpMethod.Post, requestUri)
        {
            Content = content,
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Azure Vision OCR returned {(int)response.StatusCode}: {responseBody}");
        }

        return ParseLines(responseBody, capturedRegionPhysical);
    }

    private static IReadOnlyList<TranslationLine> ParseLines(
        string responseBody,
        PhysicalRect capturedRegionPhysical)
    {
        using JsonDocument document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("readResult", out JsonElement readResult) ||
            !readResult.TryGetProperty("blocks", out JsonElement blocks))
        {
            return Array.Empty<TranslationLine>();
        }

        List<TranslationLine> lines = new();
        int lineIndex = 0;
        foreach (JsonElement block in blocks.EnumerateArray())
        {
            if (!block.TryGetProperty("lines", out JsonElement blockLines))
            {
                continue;
            }

            foreach (JsonElement line in blockLines.EnumerateArray())
            {
                string text = line.TryGetProperty("text", out JsonElement textElement)
                    ? textElement.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(text) ||
                    !line.TryGetProperty("boundingPolygon", out JsonElement polygon))
                {
                    continue;
                }

                List<PhysicalPoint> vertices = ParsePolygon(polygon, capturedRegionPhysical);
                if (vertices.Count == 0)
                {
                    continue;
                }

                PhysicalRect bounds = GetBounds(vertices);
                double confidence = 1.0;
                List<RecognizedWord> recognizedWords = new();
                if (line.TryGetProperty("words", out JsonElement words))
                {
                    double sum = 0;
                    int count = 0;
                    int wordIndex = 0;
                    foreach (JsonElement word in words.EnumerateArray())
                    {
                        string wordText = word.TryGetProperty("text", out JsonElement wordTextElement)
                            ? wordTextElement.GetString() ?? string.Empty
                            : string.Empty;
                        double wordConfidence = 1.0;
                        if (word.TryGetProperty("confidence", out JsonElement confidenceElement) &&
                            confidenceElement.TryGetDouble(out double value))
                        {
                            wordConfidence = Math.Clamp(value, 0, 1);
                            sum += wordConfidence;
                            count++;
                        }

                        if (!string.IsNullOrWhiteSpace(wordText) &&
                            word.TryGetProperty("boundingPolygon", out JsonElement wordPolygon))
                        {
                            List<PhysicalPoint> wordVertices = ParsePolygon(wordPolygon, capturedRegionPhysical);
                            if (wordVertices.Count > 0)
                            {
                                recognizedWords.Add(new RecognizedWord(
                                    wordText.Trim(),
                                    GetBounds(wordVertices),
                                    lineIndex,
                                    wordIndex,
                                    wordConfidence,
                                    wordVertices));
                            }
                        }

                        wordIndex++;
                    }

                    if (count > 0)
                    {
                        confidence = sum / count;
                    }
                }

                PhysicalRect resolvedBounds = OverlayLayoutHelper.ResolveOcrLineBounds(bounds, recognizedWords);
                if (resolvedBounds != bounds)
                {
                    bounds = resolvedBounds;
                    vertices =
                    [
                        new PhysicalPoint(bounds.Left, bounds.Top),
                        new PhysicalPoint(bounds.Right, bounds.Top),
                        new PhysicalPoint(bounds.Right, bounds.Bottom),
                        new PhysicalPoint(bounds.Left, bounds.Bottom),
                    ];
                }

                lines.Add(new TranslationLine(text.Trim(), bounds, confidence, vertices, Words: recognizedWords));
                lineIndex++;
            }
        }

        return lines;
    }

    private static List<PhysicalPoint> ParsePolygon(JsonElement polygon, PhysicalRect region)
    {
        List<PhysicalPoint> points = new();
        foreach (JsonElement point in polygon.EnumerateArray())
        {
            if (point.ValueKind == JsonValueKind.Number)
            {
                continue;
            }

            if (point.TryGetProperty("x", out JsonElement x) &&
                point.TryGetProperty("y", out JsonElement y) &&
                x.TryGetDouble(out double pointX) &&
                y.TryGetDouble(out double pointY))
            {
                points.Add(new PhysicalPoint(region.X + pointX, region.Y + pointY));
            }
        }

        return points;
    }

    private static PhysicalRect GetBounds(IReadOnlyList<PhysicalPoint> points)
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        foreach (PhysicalPoint point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new PhysicalRect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
