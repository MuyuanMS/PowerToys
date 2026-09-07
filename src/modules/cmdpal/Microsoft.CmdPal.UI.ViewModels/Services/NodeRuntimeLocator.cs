// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.CmdPal.UI.ViewModels.Services;

/// <summary>
/// Resolves an absolute path to the Node.js runtime (<c>node.exe</c>) by probing the
/// process PATH. Launching an explicit, validated absolute path rather than the bare
/// name <c>node</c> keeps <see cref="System.Diagnostics.Process"/> from resolving
/// <c>node.exe</c> out of the spawning process's working directory (which for a JS
/// extension is the extension's own, untrusted, folder) or via other implicit search
/// locations. It also lets the caller surface a specific "Node.js not found" error
/// instead of a generic Win32 launch failure.
/// </summary>
internal static class NodeRuntimeLocator
{
    private const string NodeExecutableName = "node.exe";

    /// <summary>
    /// Resolves <c>node.exe</c> from the current process PATH.
    /// </summary>
    /// <returns>The absolute path to <c>node.exe</c>, or <see langword="null"/> when it is not on PATH.</returns>
    internal static string? ResolveNodeExecutable() => ResolveNodeExecutable(GetPathDirectories());

    /// <summary>
    /// Resolves <c>node.exe</c> from an explicit ordered list of directories. Exposed for testing.
    /// </summary>
    /// <param name="pathDirectories">The directories to probe, in priority order.</param>
    /// <returns>The absolute path to the first existing <c>node.exe</c>, or <see langword="null"/>.</returns>
    internal static string? ResolveNodeExecutable(IReadOnlyList<string> pathDirectories)
    {
        ArgumentNullException.ThrowIfNull(pathDirectories);

        foreach (var directory in pathDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            try
            {
                var canonicalDirectory = Path.GetFullPath(directory);
                var candidate = Path.GetFullPath(Path.Combine(canonicalDirectory, NodeExecutableName));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Malformed PATH entry; skip it.
                continue;
            }
        }

        return null;
    }

    internal static bool IsCompatible(string nodeExecutable, string? requirement, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(requirement))
        {
            return true;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = nodeExecutable,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                reason = "Node.js version could not be determined.";
                return false;
            }

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    standardOutput.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    standardError.AppendLine(args.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                reason = "Node.js version probe timed out.";
                return false;
            }
            process.WaitForExit();

            var output = standardOutput.ToString().Trim();
            if (!Version.TryParse(output.TrimStart('v'), out var actual))
            {
                reason = $"Node.js returned an invalid version '{output}'. {standardError}".Trim();
                return false;
            }

            foreach (var clause in requirement.Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryMatchClause(actual, clause))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or FileNotFoundException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            reason = $"Node.js version could not be determined: {ex.Message}";
            return false;
        }

        reason ??= $"Node.js does not satisfy the declared engine requirement '{requirement}'.";
        return false;
    }

    private static bool TryMatchClause(Version actual, string clause)
    {
        var hyphen = clause.IndexOf(" - ", StringComparison.Ordinal);
        if (hyphen >= 0)
        {
            return TryMatchToken(actual, $">={clause[..hyphen].Trim()}")
                && TryMatchToken(actual, $"<={clause[(hyphen + 3)..].Trim()}");
        }

        var tokens = clause.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        return tokens.All(token => TryMatchToken(actual, token));
    }

    private static bool TryMatchToken(Version actual, string token)
    {
        var op = token.StartsWith(">=") || token.StartsWith("<=")
            ? token[..2]
            : token.Length > 0 && "> < = ^ ~".Contains(token[0])
                ? token[..1]
                : string.Empty;
        var versionText = token.TrimStart('>', '<', '=', '^', '~');
        var wildcard = versionText.IndexOfAny(['x', 'X', '*']);
        if (wildcard >= 0)
        {
            var wildcardParts = versionText[..wildcard].TrimEnd('.');
            if (!int.TryParse(wildcardParts.Split('.')[0], out var wildcardMajor))
            {
                return false;
            }

            var lower = new Version(wildcardMajor, 0);
            var upper = new Version(wildcardMajor + 1, 0);
            return actual.CompareTo(lower) >= 0 && actual.CompareTo(upper) < 0;
        }

        var parts = versionText.Split('.');
        if (parts.Length > 3 || !int.TryParse(parts[0], out var major))
        {
            return false;
        }
        var hasMinor = parts.Length > 1 && int.TryParse(parts[1], out var minor);
        var hasPatch = parts.Length > 2 && int.TryParse(parts[2], out var patch);
        if ((parts.Length > 1 && !hasMinor) || (parts.Length > 2 && !hasPatch))
        {
            return false;
        }
        var requested = new Version(major, hasMinor ? minor : 0, hasPatch ? patch : 0);
        if (parts.Length < 3 && op is "<=")
        {
            var upper = parts.Length == 1
                ? new Version(major + 1, 0)
                : new Version(major, minor + 1);
            return actual.CompareTo(upper) < 0;
        }
        if (parts.Length < 3 && op is ">")
        {
            var lower = parts.Length == 1
                ? new Version(major + 1, 0)
                : new Version(major, minor + 1);
            return actual.CompareTo(lower) >= 0;
        }
        if (parts.Length < 3 && op is "" )
        {
            var upper = parts.Length == 1
                ? new Version(major + 1, 0)
                : new Version(major, minor + 1);
            return actual.CompareTo(requested) >= 0 && actual.CompareTo(upper) < 0;
        }

        return op switch
        {
            ">=" => actual.CompareTo(requested) >= 0,
            "<=" => actual.CompareTo(requested) <= 0,
            ">" => actual.CompareTo(requested) > 0,
            "<" => actual.CompareTo(requested) < 0,
            "^" => actual.CompareTo(requested) >= 0 && actual.Major == requested.Major,
            "~" => actual.CompareTo(requested) >= 0 && actual.Major == requested.Major && actual.Minor == requested.Minor,
            "=" or "" => actual.CompareTo(requested) == 0,
            _ => false,
        };
    }

    private static IReadOnlyList<string> GetPathDirectories()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return [];
        }

        return pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
