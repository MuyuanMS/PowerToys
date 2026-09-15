// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using EnvironmentVariablesUILib.Models;

namespace EnvironmentVariablesUILib.Helpers;

/// <summary>
/// Centralises comparison logic so that Windows' case-insensitive name semantics are
/// applied consistently throughout the application, rather than each call site
/// independently selecting a StringComparison value.
/// </summary>
internal static class EnvironmentVariableComparisonHelper
{
    /// <summary>
    /// Compare variable name strings. Windows treats environment variable names case-
    /// insensitively: "PATH", "Path" and "path" all refer to the same variable.
    /// </summary>
    internal static bool NamesEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compare environment variable entries. Names are compared case-insensitively,
    /// values are compared case-sensitively.
    /// </summary>
    internal static bool EntriesEqual(Variable left, Variable right) =>
        left is not null
        && right is not null
        && NamesEqual(left.Name, right.Name)
        && string.Equals(left.Values, right.Values, StringComparison.Ordinal);

    /// <summary>
    /// Groups environment variables by name, ignoring case, and returns only those groups
    /// that contain logical duplicates, e.g. "Path" and "PATH". This may occur due to
    /// legacy tools or direct registry edits.
    /// </summary>
    internal static IEnumerable<IGrouping<string, Variable>> GetDuplicateNameGroups(IEnumerable<Variable> variables) =>
        variables.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

    internal static string RemoveDuplicatePathEntries(string value, IEnumerable<Variable> variables = null, Variable editedVariable = null)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in variables ?? Enumerable.Empty<Variable>())
        {
            environment[variable.Name] = variable.Values;
        }

        if (editedVariable != null)
        {
            environment[editedVariable.Name] = editedVariable.Values;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return string.Join(';', value.Split(';').Where(entry => seen.Add(NormalizePathEntry(entry, environment))));
    }

    internal static IEnumerable<Variable> BuildVariablesForPathDeduplication(
        IEnumerable<Variable> systemVariables,
        IEnumerable<Variable> userVariables,
        ProfileVariablesSet appliedProfile,
        ProfileVariablesSet editingProfile,
        Variable originalVariable = null)
    {
        var systemVariableMap = (systemVariables ?? Enumerable.Empty<Variable>())
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var variables = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in systemVariableMap.Values)
        {
            variables[variable.Name] = variable;
        }

        foreach (var variable in userVariables ?? Enumerable.Empty<Variable>())
        {
            variables[variable.Name] = variable;
        }

        if (appliedProfile != null)
        {
            foreach (var profileVariable in appliedProfile.Variables)
            {
                var backupName = EnvironmentVariablesHelper.GetBackupVariableName(profileVariable, appliedProfile.Name);
                if (!ReferenceEquals(appliedProfile, editingProfile))
                {
                    if (variables.TryGetValue(backupName, out var backupVariable))
                    {
                        variables[profileVariable.Name] = new Variable(
                            profileVariable.Name,
                            backupVariable.Values,
                            backupVariable.ParentType);
                    }
                    else
                    {
                        RestoreLowerScopeVariable(variables, systemVariableMap, profileVariable.Name);
                    }
                }

                variables.Remove(backupName);
            }
        }

        if (originalVariable != null)
        {
            RestoreLowerScopeVariable(variables, systemVariableMap, originalVariable.Name);
        }

        if (editingProfile != null)
        {
            foreach (var variable in editingProfile.Variables)
            {
                if (!ReferenceEquals(variable, originalVariable))
                {
                    variables[variable.Name] = variable;
                }
            }
        }

        return variables.Values;
    }

    private static void RestoreLowerScopeVariable(
        IDictionary<string, Variable> variables,
        IReadOnlyDictionary<string, Variable> systemVariables,
        string name)
    {
        if (systemVariables.TryGetValue(name, out var systemVariable))
        {
            variables[name] = systemVariable;
        }
        else
        {
            variables.Remove(name);
        }
    }

    private static string NormalizePathEntry(string entry, IReadOnlyDictionary<string, string> variables)
    {
        var expanded = ExpandEnvironmentVariables(entry.Trim(), variables, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var normalizedSeparators = expanded.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string previous;
        do
        {
            previous = normalizedSeparators;
            normalizedSeparators = Path.TrimEndingDirectorySeparator(previous);
        }
        while (!string.Equals(previous, normalizedSeparators, StringComparison.Ordinal));

        return normalizedSeparators;
    }

    private static string ExpandEnvironmentVariables(string value, IReadOnlyDictionary<string, string> variables, HashSet<string> expansionStack)
    {
        return Regex.Replace(value, "%([^%]+)%", match =>
        {
            string name = match.Groups[1].Value;
            if (!expansionStack.Add(name))
            {
                return match.Value;
            }

            try
            {
                string replacement = variables.TryGetValue(name, out var definedValue)
                    ? definedValue
                    : Environment.GetEnvironmentVariable(name);

                return replacement == null
                    ? match.Value
                    : ExpandEnvironmentVariables(replacement, variables, expansionStack);
            }
            finally
            {
                expansionStack.Remove(name);
            }
        });
    }
}
