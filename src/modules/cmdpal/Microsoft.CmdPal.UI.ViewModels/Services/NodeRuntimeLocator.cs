// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

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

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (!Version.TryParse(output.TrimStart('v'), out var actual))
        {
            reason = $"Node.js returned an invalid version '{output}'.";
            return false;
        }

        foreach (var clause in requirement.Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryMatchClause(actual, clause))
            {
                return true;
            }
        }

        reason = $"Node.js {actual} does not satisfy the declared engine requirement '{requirement}'.";
        return false;
    }

    private static bool TryMatchClause(Version actual, string clause)
    {
        var value = clause.Trim();
        var op = value.StartsWith(">=") || value.StartsWith("<=")
            ? value[..2]
            : value.Length > 0 && "> < = ^ ~".Contains(value[0])
                ? value[..1]
                : string.Empty;
        var versionText = value.TrimStart('>', '<', '=', '^', '~', ' ');
        if (!Version.TryParse(versionText, out var requested))
        {
            return false;
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
