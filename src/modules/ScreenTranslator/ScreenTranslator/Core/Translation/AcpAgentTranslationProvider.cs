// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator.Core.Translation;

/// <summary>
/// Experimental ACP v1 provider. It grants the agent no filesystem, terminal, or elicitation capabilities.
/// </summary>
public sealed class AcpAgentTranslationProvider : ITranslationProvider, IDisposable
{
    private readonly string _command;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;
    private string? _sessionId;
    private int _messageId;
    private bool _disposed;

    public AcpAgentTranslationProvider(string command)
    {
        _command = command?.Trim() ?? string.Empty;
    }

    public string ProviderId => "acp-agent";

    public string DisplayName => "ACP Agent (Experimental)";

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0)
        {
            return new TranslationResult(Array.Empty<TranslatedLine>(), true, TargetLanguage: request.TargetLanguage);
        }

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await EnsureSessionAsync(cancellationToken);

            int promptRequestId = ++_messageId;
            object promptParameters = new
            {
                sessionId = _sessionId,
                prompt = new[] { new { type = "text", text = BuildPrompt(request) } },
            };
            await SendAsync(
                _input!,
                promptRequestId,
                "session/prompt",
                promptParameters,
                cancellationToken);

            StringBuilder responseText = new();
            await ReadPromptAsync(_output!, promptRequestId, responseText, cancellationToken);
            return ParseTranslationResponse(request, responseText.ToString());
        }
        catch (OperationCanceledException)
        {
            DisposeProcess();
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
        {
            DisposeProcess();
            return Failure($"ACP translation failed: {ex.Message}");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && _input is not null && _output is not null && !string.IsNullOrWhiteSpace(_sessionId))
        {
            await CreateSessionAsync(cancellationToken);
            return;
        }

        DisposeProcess();
        if (!TrySplitCommand(_command, out string fileName, out string arguments))
        {
            throw new InvalidOperationException("ACP agent command is not configured.");
        }

        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("ACP agent process could not be started.");
        }

        _process = process;
        _input = process.StandardInput;
        _output = process.StandardOutput;
        _messageId = 0;

        int initializeRequestId = _messageId++;
        await SendAsync(
            _input,
            initializeRequestId,
            "initialize",
            new
            {
                protocolVersion = 1,
                clientCapabilities = new
                {
                    fs = new { readTextFile = false, writeTextFile = false },
                    terminal = false,
                },
                clientInfo = new { name = "PowerToys Screen Translator", title = "Screen Translator", version = "0.1" },
            },
            cancellationToken);
        using (JsonDocument initializeResponse = await ReadResponseAsync(_output, initializeRequestId, cancellationToken))
        {
        }

        await CreateSessionAsync(cancellationToken);
    }

    private async Task CreateSessionAsync(CancellationToken cancellationToken)
    {
        int sessionRequestId = ++_messageId;
        await SendAsync(
            _input!,
            sessionRequestId,
            "session/new",
            new
            {
                cwd = Environment.CurrentDirectory,
                mcpServers = Array.Empty<object>(),
            },
            cancellationToken);
        using JsonDocument sessionResponse = await ReadResponseAsync(_output!, sessionRequestId, cancellationToken);
        _sessionId = GetString(sessionResponse.RootElement, "result", "sessionId");
        if (string.IsNullOrWhiteSpace(_sessionId))
        {
            throw new IOException("ACP agent did not return a session ID.");
        }
    }

    private static async Task SendAsync(StreamWriter input, int id, string method, object parameters, CancellationToken cancellationToken)
    {
        string message = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        await input.WriteLineAsync(message.AsMemory(), cancellationToken);
        await input.FlushAsync(cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponseAsync(StreamReader output, int expectedId, CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line = await output.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new IOException("ACP agent closed its output before responding.");
            }

            JsonDocument message = JsonDocument.Parse(line);
            if (message.RootElement.TryGetProperty("id", out JsonElement id) &&
                id.ValueKind == JsonValueKind.Number &&
                id.GetInt32() == expectedId)
            {
                return message;
            }

            message.Dispose();
        }
    }

    private static async Task ReadPromptAsync(
        StreamReader output,
        int expectedId,
        StringBuilder responseText,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line = await output.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new IOException("ACP agent closed its output during translation.");
            }

            using JsonDocument message = JsonDocument.Parse(line);
            JsonElement root = message.RootElement;
            if (root.TryGetProperty("params", out JsonElement parameters) &&
                parameters.TryGetProperty("update", out JsonElement update) &&
                update.TryGetProperty("sessionUpdate", out JsonElement kind) &&
                kind.GetString() == "agent_message_chunk" &&
                update.TryGetProperty("content", out JsonElement content) &&
                content.TryGetProperty("text", out JsonElement text))
            {
                responseText.Append(text.GetString());
            }

            if (root.TryGetProperty("id", out JsonElement id) &&
                id.ValueKind == JsonValueKind.Number &&
                id.GetInt32() == expectedId)
            {
                return;
            }
        }
    }

    private static TranslationResult ParseTranslationResponse(TranslationRequest request, string response)
    {
        int start = response.IndexOf('{');
        int end = response.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return Failure("ACP agent returned no structured translation response.");
        }

        using JsonDocument document = JsonDocument.Parse(response[start..(end + 1)]);
        if (!document.RootElement.TryGetProperty("translations", out JsonElement translations) ||
            translations.ValueKind != JsonValueKind.Array)
        {
            return Failure("ACP agent response did not contain a translations array.");
        }

        Dictionary<string, string> translatedText = new(StringComparer.Ordinal);
        foreach (JsonElement item in translations.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out JsonElement id) ||
                id.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("text", out JsonElement text) ||
                text.ValueKind != JsonValueKind.String)
            {
                return Failure("ACP agent returned an invalid translation item.");
            }

            string? idValue = id.GetString();
            if (string.IsNullOrEmpty(idValue) || !translatedText.TryAdd(idValue, text.GetString() ?? string.Empty))
            {
                return Failure("ACP agent returned duplicate or empty translation IDs.");
            }
        }

        if (translatedText.Count != request.Lines.Count)
        {
            return Failure($"ACP agent returned {translatedText.Count} lines for {request.Lines.Count} inputs.");
        }

        List<TranslatedLine> lines = new();
        for (int i = 0; i < request.Lines.Count; i++)
        {
            if (!translatedText.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out string? translation))
            {
                return Failure($"ACP agent response is missing translation ID {i}.");
            }

            TranslationLine source = request.Lines[i];
            lines.Add(new TranslatedLine(source.Text, translation, source.BoundingBox, source.Confidence, source.PolygonVertices, source.SourceLineCount));
        }

        return new TranslationResult(lines, true, TargetLanguage: request.TargetLanguage, SourceLanguage: request.SourceLanguage);
    }

    private static string BuildPrompt(TranslationRequest request)
    {
        StringBuilder prompt = new();
        prompt.AppendLine("Translate each input line from the source language to the target language.");
        prompt.AppendLine(string.Format(CultureInfo.InvariantCulture, "Source language: {0}", request.SourceLanguage));
        prompt.AppendLine(string.Format(CultureInfo.InvariantCulture, "Target language: {0}", request.TargetLanguage));
        prompt.AppendLine("Return JSON only, with exactly this shape: {\"translations\":[{\"id\":\"0\",\"text\":\"...\"}]}.");
        prompt.AppendLine(string.Format(CultureInfo.InvariantCulture, "Return exactly {0} translation items in the same order. Do not explain.", request.Lines.Count));
        for (int i = 0; i < request.Lines.Count; i++)
        {
            prompt.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{{\"id\":\"{0}\",\"text\":{1}}}",
                i,
                JsonSerializer.Serialize(request.Lines[i].Text)));
        }

        return prompt.ToString();
    }

    private static TranslationResult Failure(string message) =>
        new(Array.Empty<TranslatedLine>(), false, message);

    private static string? GetString(JsonElement root, string property, string nestedProperty)
    {
        return root.TryGetProperty(property, out JsonElement outer) &&
            outer.TryGetProperty(nestedProperty, out JsonElement value)
                ? value.GetString()
                : null;
    }

    private static bool TrySplitCommand(string command, out string fileName, out string arguments)
    {
        fileName = string.Empty;
        arguments = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        command = command.Trim();
        if (command[0] == '"')
        {
            int closingQuote = command.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                return false;
            }

            fileName = command[1..closingQuote];
            arguments = command[(closingQuote + 1)..].Trim();
        }
        else
        {
            int separator = command.IndexOf(' ');
            fileName = separator < 0 ? command : command[..separator];
            arguments = separator < 0 ? string.Empty : command[(separator + 1)..].Trim();
        }

        return true;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void DisposeProcess()
    {
        StreamWriter? input = _input;
        StreamReader? output = _output;
        Process? process = _process;
        _input = null;
        _output = null;
        _process = null;
        _sessionId = null;

        input?.Dispose();
        output?.Dispose();
        if (process is not null)
        {
            TryKill(process);
            process.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeProcess();
        _requestLock.Dispose();
    }
}
