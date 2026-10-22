// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.MouseWithoutBorders.UITests;

internal static class RunFiles
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IEnumerable<string> EvidenceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path) is ".json" or ".png"))
        {
            yield return file;
        }

        // Endpoint workers write only this run's filtered/redacted log excerpts here.
        // Do not recurse into recordings, requests, or private recovery directories.
        var logs = Path.Combine(directory, "logs");
        if (Directory.Exists(logs))
        {
            foreach (var file in Directory.EnumerateFiles(logs, "*", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }
    }

    public static string PersistentResultsRoot(string? testRunDirectory)
    {
        if (string.IsNullOrWhiteSpace(testRunDirectory))
        {
            return Path.Combine(Environment.CurrentDirectory, "TestResults");
        }

        // MSTest removes successful deployment trees, including attached files.
        return Directory.GetParent(Path.GetFullPath(testRunDirectory))?.FullName
            ?? throw new InvalidOperationException("The test deployment directory has no parent for persistent evidence.");
    }

    public static void Write(string path, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Write(bytes);
            stream.Flush();
        }

        // Unique messages become visible before the writer has finished. A separate
        // marker prevents redirected readers from caching an incomplete first read.
        using var published = new FileStream(path + ".ready", FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    }

    public static JsonObject Read(string path)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonNode.Parse(stream) as JsonObject ?? throw new JsonException("Expected a JSON object.");
            }
            catch (Exception error) when ((error is IOException || error is JsonException) && timer.Elapsed < TimeSpan.FromSeconds(3))
            {
                Thread.Sleep(50);
            }
        }
    }

    public static void Wait(Func<bool> condition, TimeSpan timeout, string description, Action? tick = null)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            tick?.Invoke();
            if (condition())
            {
                return;
            }

            Thread.Sleep(200);
        }
        while (timer.Elapsed < timeout);
        throw new TimeoutException(description);
    }

    public static void RemoveOwnedDirectory(string path, TimeSpan timeout)
    {
        Wait(
            () =>
            {
                if (!Directory.Exists(path))
                {
                    return true;
                }

                _ = WinAppSandboxPayload.PlainFiles(path).ToArray();
                try
                {
                    Directory.Delete(path, recursive: true);
                    return true;
                }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 or 145)
                {
                    // HCS can release a mapped folder shortly after the exact Sandbox
                    // process/provider is absent. Do not retry unrelated access failures.
                    return false;
                }
            },
            timeout,
            "An owned directory remained in use after bounded resource cleanup: " + path);
    }

    public static void RemoveEmptyRunControlParent(string runId, TimeSpan timeout)
    {
        if (!Guid.TryParseExact(runId, "D", out var parsedRunId))
        {
            throw new ArgumentException("The run identifier must be a GUID.", nameof(runId));
        }

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "PowerToysUiTestControl",
            parsedRunId.ToString());
        Wait(
            () =>
            {
                if (!Directory.Exists(path))
                {
                    return true;
                }

                _ = WinAppSandboxPayload.PlainFiles(path).ToArray();
                if (Directory.EnumerateFileSystemEntries(path).Any())
                {
                    throw new InvalidOperationException("Refusing to remove a nonempty run control parent: " + path);
                }

                try
                {
                    Directory.Delete(path, recursive: false);
                    return true;
                }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 or 145)
                {
                    return false;
                }
            },
            timeout,
            "An empty run control parent remained in use after bounded cleanup: " + path);
    }
}
