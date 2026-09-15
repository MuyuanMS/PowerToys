// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

using EnvironmentVariablesUILib.Helpers;
using EnvironmentVariablesUILib.Models;

namespace EnvironmentVariablesUILib.UnitTests.Helpers;

[TestClass]
public class EnvironmentVariableComparisonHelperTests
{
    [TestMethod]
    public void NamesEqual_IgnoresCase()
    {
        Assert.IsTrue(EnvironmentVariableComparisonHelper.NamesEqual("PATH", "path"));
    }

    [TestMethod]
    public void EntriesEqual_NameIsCaseInsensitiveButValueIsOrdinal()
    {
        var upperName = new Variable("PATH", "Value", VariablesSetType.User);
        var lowerNameSameValue = new Variable("path", "Value", VariablesSetType.System);
        var lowerNameDifferentValueCase = new Variable("path", "value", VariablesSetType.System);

        Assert.IsTrue(EnvironmentVariableComparisonHelper.EntriesEqual(upperName, lowerNameSameValue));
        Assert.IsFalse(EnvironmentVariableComparisonHelper.EntriesEqual(upperName, lowerNameDifferentValueCase));
    }

    [TestMethod]
    public void GetDuplicateNameGroups_ReturnsLegacyEntriesThatDifferOnlyByCase()
    {
        var first = new Variable("PATH", "SystemValue", VariablesSetType.System);
        var second = new Variable("path", "UserValue", VariablesSetType.User);
        var variables = new[]
        {
            first,
            second,
            new Variable("TEMP", "TempValue", VariablesSetType.User),
        };

        var duplicates = EnvironmentVariableComparisonHelper.GetDuplicateNameGroups(variables).ToList();

        Assert.AreEqual(1, duplicates.Count);
        CollectionAssert.AreEquivalent(new[] { first, second }, duplicates[0].ToList());
    }

    [TestMethod]
    public void RemoveDuplicatePathEntries_PreservesFirstOccurrenceAndOrder()
    {
        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"C:\Tools;C:\Windows;c:\tools;C:\Program Files;C:\WINDOWS");

        Assert.AreEqual(@"C:\Tools;C:\Windows;C:\Program Files", result);
    }

    [TestMethod]
    public void RemoveDuplicatePathEntries_NormalizesSeparatorsAndTrailingSeparators()
    {
        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"C:\Tools\\;C:/Tools;C:\Other");

        Assert.AreEqual(@"C:\Tools\\;C:\Other", result);
    }

    [TestMethod]
    public void RemoveDuplicatePathEntries_NormalizesUncShareRootSeparators()
    {
        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"\\server\share\;\\server\share");

        Assert.AreEqual(@"\\server\share\", result);
    }

    [TestMethod]
    public void RemoveDuplicatePathEntries_DoesNotTreatDeviceDriveRootsAsUncShares()
    {
        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"\\?\C:\\;\\?\C:\");

        Assert.AreEqual(@"\\?\C:\\", result);
    }

    [TestMethod]
    public void RemoveDuplicatePathEntries_ExpandsEditedVariableDefinitions()
    {
        var variables = new[]
        {
            new Variable("ROOT", @"C:\Tools", VariablesSetType.Profile),
        };

        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"%ROOT%;C:\Tools;C:\Other",
            variables);

        Assert.AreEqual(@"%ROOT%;C:\Other", result);
    }

    [TestMethod]
    public void BuildVariablesForPathDeduplication_ReconstructsBaselineWhenEditingInactiveProfile()
    {
        var activeProfile = new ProfileVariablesSet(Guid.NewGuid(), "Active");
        activeProfile.Variables.Add(new Variable("ROOT", @"C:\Applied", VariablesSetType.Profile));

        var inactiveProfile = new ProfileVariablesSet(Guid.NewGuid(), "Inactive");
        inactiveProfile.Variables.Add(new Variable("PATH", @"%ROOT%;C:\Applied", VariablesSetType.Profile));

        var variables = EnvironmentVariableComparisonHelper.BuildVariablesForPathDeduplication(
            Array.Empty<Variable>(),
            new[]
            {
                new Variable("ROOT", @"C:\Applied", VariablesSetType.User),
                new Variable("ROOT_PowerToys_Active", @"C:\Base", VariablesSetType.User),
            },
            activeProfile,
            inactiveProfile,
            inactiveProfile.Variables[0]);

        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            inactiveProfile.Variables[0].Values,
            variables);

        Assert.AreEqual(@"%ROOT%;C:\Applied", result);
    }

    [TestMethod]
    public void BuildVariablesForPathDeduplication_PreservesUserValueWhenRenamingInactiveProfileVariable()
    {
        var inactiveProfile = new ProfileVariablesSet(Guid.NewGuid(), "Inactive");
        var originalVariable = new Variable("ROOT", @"C:\Profile", VariablesSetType.Profile);
        inactiveProfile.Variables.Add(originalVariable);

        var variables = EnvironmentVariableComparisonHelper.BuildVariablesForPathDeduplication(
            new[]
            {
                new Variable("ROOT", @"C:\Machine", VariablesSetType.System),
            },
            new[]
            {
                new Variable("ROOT", @"C:\User", VariablesSetType.User),
            },
            appliedProfile: null,
            editingProfile: inactiveProfile,
            originalVariable: originalVariable);

        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"%ROOT%;C:\User",
            variables);

        Assert.AreEqual(@"%ROOT%", result);
    }

    [TestMethod]
    public void BuildVariablesForPathDeduplication_RestoresMachineValueForMachineOnlyOverride()
    {
        var activeProfile = new ProfileVariablesSet(Guid.NewGuid(), "Active");
        activeProfile.Variables.Add(new Variable("ROOT", @"C:\Applied", VariablesSetType.Profile));

        var variables = EnvironmentVariableComparisonHelper.BuildVariablesForPathDeduplication(
            new[]
            {
                new Variable("ROOT", @"C:\Base", VariablesSetType.System),
            },
            new[]
            {
                new Variable("ROOT", @"C:\Applied", VariablesSetType.User),
            },
            activeProfile,
            editingProfile: null);

        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"%ROOT%;C:\Applied",
            variables);

        Assert.AreEqual(@"%ROOT%;C:\Applied", result);
    }

    [TestMethod]
    public void BuildVariablesForPathDeduplication_RemovesOriginalDefinitionWhenVariableIsRenamed()
    {
        var originalVariable = new Variable("ROOT", @"C:\Tools", VariablesSetType.User);
        var editedVariable = new Variable("PATH", @"%ROOT%;C:\Tools", VariablesSetType.User);
        var unavailableVariableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var variables = EnvironmentVariableComparisonHelper.BuildVariablesForPathDeduplication(
            Array.Empty<Variable>(),
            new[] { originalVariable },
            appliedProfile: null,
            editingProfile: null,
            originalVariable,
            unavailableVariableNames);

        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            editedVariable.Values,
            variables,
            editedVariable,
            unavailableVariableNames);

        Assert.AreEqual(@"%ROOT%;C:\Tools", result);
    }

    [TestMethod]
    public void BuildVariablesForPathDeduplication_UsesOnlyMachineScopeForMachineEdits()
    {
        var originalVariable = new Variable("PATH", @"%ROOT%;C:\User", VariablesSetType.System);

        var variables = EnvironmentVariableComparisonHelper.BuildVariablesForPathDeduplication(
            new[]
            {
                new Variable("ROOT", @"C:\Machine", VariablesSetType.System),
            },
            new[]
            {
                new Variable("ROOT", @"C:\User", VariablesSetType.User),
            },
            appliedProfile: null,
            editingProfile: null,
            originalVariable);

        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            originalVariable.Values,
            variables,
            originalVariable);

        Assert.AreEqual(@"%ROOT%;C:\User", result);
    }

    [TestMethod]
    public void RemoveDuplicatePathEntries_DoesNotUseProcessValueForRemovedDefinition()
    {
        var originalValue = Environment.GetEnvironmentVariable("ROOT");
        try
        {
            Environment.SetEnvironmentVariable("ROOT", @"C:\Tools");
            var originalVariable = new Variable("ROOT", @"C:\Tools", VariablesSetType.User);
            var editedVariable = new Variable("PATH", @"%ROOT%;C:\Tools", VariablesSetType.User);
            var unavailableVariableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var variables = EnvironmentVariableComparisonHelper.BuildVariablesForPathDeduplication(
                Array.Empty<Variable>(),
                new[] { originalVariable },
                appliedProfile: null,
                editingProfile: null,
                originalVariable,
                unavailableVariableNames);

            var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
                editedVariable.Values,
                variables,
                editedVariable,
                unavailableVariableNames);

            Assert.AreEqual(@"%ROOT%;C:\Tools", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROOT", originalValue);
        }
    }

    [TestMethod]
    public void BuildVariablesForPathDeduplication_PreservesActiveProfileBackupWhenRenaming()
    {
        var activeProfile = new ProfileVariablesSet(Guid.NewGuid(), "Active");
        var originalVariable = new Variable("ROOT", @"C:\Applied", VariablesSetType.Profile);
        activeProfile.Variables.Add(originalVariable);

        var variables = EnvironmentVariableComparisonHelper.BuildVariablesForPathDeduplication(
            new[]
            {
                new Variable("ROOT", @"C:\Machine", VariablesSetType.System),
            },
            new[]
            {
                new Variable("ROOT", @"C:\Applied", VariablesSetType.User),
                new Variable("ROOT_PowerToys_Active", @"C:\Base", VariablesSetType.User),
            },
            activeProfile,
            activeProfile,
            originalVariable);

        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"%ROOT%;C:\Machine",
            variables);

        Assert.AreEqual(@"%ROOT%;C:\Machine", result);
    }

    [TestMethod]
    public void BuildVariablesForPathDeduplication_ReconstructsBaselineWhenEditingDefaultVariable()
    {
        var activeProfile = new ProfileVariablesSet(Guid.NewGuid(), "Active");
        activeProfile.Variables.Add(new Variable("ROOT", @"C:\Applied", VariablesSetType.Profile));

        var variables = EnvironmentVariableComparisonHelper.BuildVariablesForPathDeduplication(
            Array.Empty<Variable>(),
            new[]
            {
                new Variable("ROOT", @"C:\Applied", VariablesSetType.User),
                new Variable("ROOT_PowerToys_Active", @"C:\Base", VariablesSetType.User),
            },
            activeProfile,
            editingProfile: null,
            originalVariable: new Variable("PATH", @"%ROOT%;C:\Applied", VariablesSetType.User));

        var result = EnvironmentVariableComparisonHelper.RemoveDuplicatePathEntries(
            @"%ROOT%;C:\Applied",
            variables);

        Assert.AreEqual(@"%ROOT%;C:\Applied", result);
    }
}
