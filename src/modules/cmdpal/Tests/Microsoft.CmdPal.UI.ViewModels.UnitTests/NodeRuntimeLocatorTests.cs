// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.CmdPal.UI.ViewModels.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public class NodeRuntimeLocatorTests
{
    private static readonly string[] MalformedPathEntries = { "invalid|path" };

    [TestMethod]
    public void ResolveNodeExecutable_ReturnsFirstDirectoryThatContainsNodeExe()
    {
        var root = Path.Combine(Path.GetTempPath(), "cmdpal-node-locator-" + Guid.NewGuid().ToString("N"));
        var withoutNode = Path.Combine(root, "without");
        var withNode = Path.Combine(root, "with");
        Directory.CreateDirectory(withoutNode);
        Directory.CreateDirectory(withNode);

        var expected = Path.Combine(withNode, "node.exe");

        try
        {
            File.WriteAllText(expected, string.Empty);

            // The first directory has no node.exe, so resolution must skip it and return
            // the absolute path from the second directory rather than the bare name.
            var resolved = NodeRuntimeLocator.ResolveNodeExecutable(new[] { withoutNode, withNode });

            Assert.AreEqual(expected, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ResolveNodeExecutable_ReturnsNullWhenNodeExeIsNotPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "cmdpal-node-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var resolved = NodeRuntimeLocator.ResolveNodeExecutable(new[] { root });
            Assert.IsNull(resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ResolveNodeExecutable_SkipsMalformedPathEntries()
    {
        // A PATH entry containing invalid path characters must be skipped rather than
        // throwing, so a single bad entry cannot break node.exe resolution.
        var resolved = NodeRuntimeLocator.ResolveNodeExecutable(MalformedPathEntries);
        Assert.IsNull(resolved);
    }

    [TestMethod]
    public void ResolveNodeExecutable_ReturnsNullForEmptyDirectoryList()
    {
        Assert.IsNull(NodeRuntimeLocator.ResolveNodeExecutable(Array.Empty<string>()));
    }

    [TestMethod]
    [DoNotParallelize]
    public void ResolveNodeExecutable_RejectsCurrentAndRelativeDirectories()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDirectory.FullName, "node.exe"), string.Empty);
            Environment.CurrentDirectory = tempDirectory.FullName;

            var result = NodeRuntimeLocator.ResolveNodeExecutable([".", tempDirectory.Name]);

            Assert.IsNull(result);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            tempDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveNodeExecutable_RejectsQuotedAndMalformedDirectories()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDirectory.FullName, "node.exe"), string.Empty);

            var result = NodeRuntimeLocator.ResolveNodeExecutable(
            [
                $"\"{tempDirectory.FullName}\"",
                "\0invalid",
            ]);

            Assert.IsNull(result);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveNodeExecutable_ReturnsCanonicalAbsolutePath()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var nestedDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory.FullName, "nested"));
            var nodePath = Path.Combine(tempDirectory.FullName, "node.exe");
            File.WriteAllText(nodePath, string.Empty);

            var result = NodeRuntimeLocator.ResolveNodeExecutable(
                [Path.Combine(nestedDirectory.FullName, "..")]);

            Assert.IsNotNull(result);
            Assert.AreEqual(Path.GetFullPath(nodePath), result);
            Assert.IsTrue(Path.IsPathFullyQualified(result));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow("22.1.5", "22.1.x", true)]
    [DataRow("22.2.0", "22.1.x", false)]
    [DataRow("24.0.0", "*", true)]
    [DataRow("24.0.0", "^*", true)]
    [DataRow("23.0.0", ">=22.x", true)]
    [DataRow("21.9.0", ">=22.x", false)]
    [DataRow("22.9.0", "<=22.x", true)]
    [DataRow("23.0.0", "<=22.x", false)]
    public void MatchesRequirement_EvaluatesWildcardRanges(string actual, string requirement, bool expected)
    {
        Assert.AreEqual(expected, NodeRuntimeLocator.MatchesRequirement(Version.Parse(actual), requirement));
    }

    [TestMethod]
    [DataRow("22.8.0", "~22", true)]
    [DataRow("23.0.0", "~22", false)]
    [DataRow("22.1.5", "~22.1", true)]
    [DataRow("22.1.5", "~>22.1", true)]
    [DataRow("22.1.5", "~> 22.1", true)]
    [DataRow("22.2.0", "~22.1", false)]
    [DataRow("22.1.9", "20.10.0 - 22.x", true)]
    [DataRow("23.0.0", "20.10.0 - 22.x", false)]
    public void MatchesRequirement_EvaluatesTildeAndHyphenRanges(string actual, string requirement, bool expected)
    {
        Assert.AreEqual(expected, NodeRuntimeLocator.MatchesRequirement(Version.Parse(actual), requirement));
    }

    [TestMethod]
    [DataRow("22.1.5", "<=22.1", true)]
    [DataRow("22.2.0", "<=22.1", false)]
    [DataRow("22.2.0", ">22.1", true)]
    [DataRow("22.1.9", ">22.1", false)]
    [DataRow("22.4.0", "20 || >=22 <23", true)]
    [DataRow("23.0.0", "20 || >=22 <23", false)]
    [DataRow("22.4.0", ">= 22 < 23", true)]
    [DataRow("22.4.0", ">=22	<23", true)]
    [DataRow("23.0.0", ">= 22 < 23", false)]
    [DataRow("22.0.0", ">=22.0.0-0", true)]
    [DataRow("22.0.0", ">= v22.0.0", true)]
    [DataRow("22.0.0", "22.0.0-beta", false)]
    [DataRow("22.0.0", "22.0.0-", false)]
    [DataRow("22.5.0", "^22.1.x", true)]
    [DataRow("23.0.0", "^22.1.x", false)]
    [DataRow("22.0.0", "=>22", false)]
    [DataRow("22.0.0", ">>22", false)]
    [DataRow("22.0.0", "22.x.1", false)]
    public void MatchesRequirement_EvaluatesComparatorSetsAndMalformedRanges(string actual, string requirement, bool expected)
    {
        Assert.AreEqual(expected, NodeRuntimeLocator.MatchesRequirement(Version.Parse(actual), requirement));
    }

    [TestMethod]
    [DataRow("20.0.0", null, false)]
    [DataRow("20.0.0", ">=18", false)]
    [DataRow("22.0.0", null, true)]
    [DataRow("22.0.0", ">=22", true)]
    [DataRow("22.0.0", ">=24", false)]
    public void IsSupportedNodeVersion_EnforcesSdkMinimumAndManifestRange(string actual, string? requirement, bool expected)
    {
        Assert.AreEqual(expected, NodeRuntimeLocator.IsSupportedNodeVersion(Version.Parse(actual), requirement));
    }

    [TestMethod]
    public void IsCompatible_ReturnsReasonWhenVersionProbeCannotStart()
    {
        var result = NodeRuntimeLocator.IsCompatible(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "node.exe"),
            ">=22",
            out var reason);

        Assert.IsFalse(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(reason));
    }
}
