// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdvancedPaste.Helpers;
using AdvancedPaste.Models;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.PowerToys.Settings.UI.Library;

namespace AdvancedPaste.Services.CustomActions
{
    public sealed class AnthropicPasteProvider : IPasteAIProvider
    {
        private const int DefaultMaxOutputTokens = 4096;

        private static readonly IReadOnlyCollection<AIServiceType> SupportedTypes = new[]
        {
            AIServiceType.Anthropic,
        };

        private static readonly object ClientLock = new();
        private static AnthropicClient client;
        private static string clientFingerprint;

        public static PasteAIProviderRegistration Registration { get; } = new(SupportedTypes, config => new AnthropicPasteProvider(config));

        private readonly PasteAIConfig _config;

        public AnthropicPasteProvider(PasteAIConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            _config = config;
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public async Task<string> ProcessPasteAsync(PasteAIRequest request, CancellationToken cancellationToken, IProgress<double> progress)
        {
            ArgumentNullException.ThrowIfNull(request);

            var systemPrompt = request.SystemPrompt;
            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                throw new ArgumentException("System prompt must be provided", nameof(request));
            }

            var prompt = request.Prompt;
            var inputText = request.InputText;
            var imageBytes = request.ImageBytes;

            if (string.IsNullOrWhiteSpace(prompt) || (string.IsNullOrWhiteSpace(inputText) && imageBytes is null))
            {
                throw new ArgumentException("Prompt and input content must be provided", nameof(request));
            }

            var apiKey = _config.ApiKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("API key is required for Anthropic but was not provided.");
            }

            var modelId = _config.Model;
            if (string.IsNullOrWhiteSpace(modelId))
            {
                modelId = PasteAIProviderDefaults.GetDefaultModelName(AIServiceType.Anthropic);
            }

            var endpoint = string.IsNullOrWhiteSpace(_config.Endpoint) ? null : _config.Endpoint.Trim();

            var client = GetClient(apiKey, endpoint);

            using var chatClient = client.AsIChatClient(modelId, DefaultMaxOutputTokens);
            var messages = new List<ChatMessage>();

            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

            if (imageBytes != null)
            {
                var contentItems = new List<AIContent>();
                if (!string.IsNullOrWhiteSpace(inputText))
                {
                    contentItems.Add(new TextContent($"Clipboard Content:\n{inputText}"));
                }

                contentItems.Add(new DataContent(imageBytes, request.ImageMimeType ?? "image/png"));
                contentItems.Add(new TextContent($"User instructions:\n{prompt}\n\nOutput:"));

                messages.Add(new ChatMessage(ChatRole.User, contentItems));
            }
            else
            {
                var userMessageContent = $"""
                    User instructions:
                    {prompt}

                    Clipboard Content:
                    {inputText}

                    Output:
                    """;
                messages.Add(new ChatMessage(ChatRole.User, userMessageContent));
            }

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

            request.Usage = new AIServiceUsage((int)(response.Usage?.InputTokenCount ?? 0), (int)(response.Usage?.OutputTokenCount ?? 0));

            if (string.Equals(response.FinishReason?.Value, ChatFinishReason.Length.Value, StringComparison.Ordinal))
            {
                throw new PasteActionException(
                    ResourceLoaderInstance.ResourceLoader.GetString("AnthropicResponseTruncated"),
                    new InvalidOperationException("Anthropic response exceeded the configured output token limit."),
                    aiServiceMessage: ResourceLoaderInstance.ResourceLoader.GetString("AnthropicResponseTruncatedDetails"));
            }

            return response.Text ?? string.Empty;
        }

        private static AnthropicClient GetClient(string apiKey, string endpoint)
        {
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{endpoint ?? string.Empty}\n{apiKey}")));

            lock (ClientLock)
            {
                if (client is null || !string.Equals(clientFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    client?.Dispose();
                    client = !string.IsNullOrWhiteSpace(endpoint)
                        ? new AnthropicClient { ApiKey = apiKey, BaseUrl = endpoint }
                        : new AnthropicClient { ApiKey = apiKey };
                    clientFingerprint = fingerprint;
                }

                return client;
            }
        }
    }
}
