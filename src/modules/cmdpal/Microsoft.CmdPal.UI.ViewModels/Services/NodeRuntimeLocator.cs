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
    private static readonly Version MinimumSupportedVersion = new(22, 0, 0);

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

            if (IsSupportedNodeVersion(actual, requirement))
            {
                return true;
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

        reason ??= string.IsNullOrWhiteSpace(requirement)
            ? "Node.js 22.0.0 or newer is required."
            : $"Node.js does not satisfy the supported minimum and declared engine requirement '{requirement}'.";
        return false;
    }

    internal static bool IsSupportedNodeVersion(Version actual, string? requirement)
    {
        ArgumentNullException.ThrowIfNull(actual);

        return actual.CompareTo(MinimumSupportedVersion) >= 0
            && (string.IsNullOrWhiteSpace(requirement) || MatchesRequirement(actual, requirement));
    }

    internal static bool MatchesRequirement(Version actual, string requirement)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);

        foreach (var clause in requirement.Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryMatchClause(actual, clause))
            {
                return true;
            }
        }

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

        var rawTokens = clause.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (rawTokens.Length == 0)
        {
            return false;
        }

        var tokens = new List<string>();
        for (var index = 0; index < rawTokens.Length; index++)
        {
            if (IsComparator(rawTokens[index]))
            {
                if (index + 1 >= rawTokens.Length)
                {
                    return false;
                }

                tokens.Add(rawTokens[index] + rawTokens[++index]);
            }
            else
            {
                tokens.Add(rawTokens[index]);
            }
        }

        return tokens.All(token => TryMatchToken(actual, token));
    }

    private static bool IsComparator(string token) => token is ">" or ">=" or "<" or "<=" or "=" or "^" or "~";

    private static bool TryMatchToken(Version actual, string token)
    {
        var op = token.StartsWith(">=", StringComparison.Ordinal) || token.StartsWith("<=", StringComparison.Ordinal)
            ? token[..2]
            : token.Length > 0 && "> < = ^ ~".Contains(token[0])
                ? token[..1]
                : string.Empty;
        var versionText = token[op.Length..];
        var metadataIndex = versionText.IndexOfAny(['-', '+']);
        if (metadataIndex >= 0)
        {
            if (metadataIndex == 0)
            {
                return false;
            }

            versionText = versionText[..metadataIndex];
        }

        var parts = versionText.Split('.');
        if (parts.Length > 3 || parts.Length == 0)
        {
            return false;
        }

        var components = new int[3];
        var specifiedComponents = 0;
        var sawWildcard = false;
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index] is "x" or "X" or "*")
            {
                sawWildcard = true;
                continue;
            }

            if (sawWildcard || !int.TryParse(parts[index], out components[index]) || components[index] < 0)
            {
                return false;
            }

            specifiedComponents++;
        }

        if (sawWildcard && specifiedComponents == parts.Length)
        {
            return false;
        }

        var lower = new Version(components[0], components[1], components[2]);
        var upper = GetPartialUpperBound(components, specifiedComponents);

        if (sawWildcard)
        {
            return op switch
            {
                ">" => upper is not null && actual.CompareTo(upper) >= 0,
                ">=" => actual.CompareTo(lower) >= 0,
                "<" => actual.CompareTo(lower) < 0,
                "<=" => upper is null || actual.CompareTo(upper) < 0,
                "=" or "" or "^" or "~" => (upper is null || actual.CompareTo(upper) < 0) && actual.CompareTo(lower) >= 0,
                _ => false,
            };
        }

        if (specifiedComponents < 3 && op is "<=")
        {
            return upper is not null && actual.CompareTo(upper) < 0;
        }

        if (specifiedComponents < 3 && op is ">")
        {
            return upper is not null && actual.CompareTo(upper) >= 0;
        }

        if (specifiedComponents < 3 && op is ("" or "="))
        {
            return upper is not null && actual.CompareTo(lower) >= 0 && actual.CompareTo(upper) < 0;
        }

        return op switch
        {
            ">=" => actual.CompareTo(lower) >= 0,
            "<=" => actual.CompareTo(lower) <= 0,
            ">" => actual.CompareTo(lower) > 0,
            "<" => actual.CompareTo(lower) < 0,
            "^" => actual.CompareTo(lower) >= 0 && actual.CompareTo(GetCaretUpperBound(components)) < 0,
            "~" => actual.CompareTo(lower) >= 0 && actual.CompareTo(GetTildeUpperBound(components, specifiedComponents)) < 0,
            "=" or "" => actual.CompareTo(lower) == 0,
            _ => false,
        };
    }

    private static Version? GetPartialUpperBound(int[] components, int specifiedComponents)
    {
        return specifiedComponents switch
        {
            0 => null,
            1 => new Version(components[0] + 1, 0, 0),
            _ => new Version(components[0], components[1] + 1, 0),
        };
    }

    private static Version GetCaretUpperBound(int[] components)
    {
        if (components[0] > 0)
        {
            return new Version(components[0] + 1, 0, 0);
        }

        if (components[1] > 0)
        {
            return new Version(0, components[1] + 1, 0);
        }

        return new Version(0, 0, components[2] + 1);
    }

    private static Version GetTildeUpperBound(int[] components, int specifiedComponents)
    {
        return specifiedComponents == 1
            ? new Version(components[0] + 1, 0, 0)
            : new Version(components[0], components[1] + 1, 0);
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
