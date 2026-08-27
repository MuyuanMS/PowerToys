// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortcutGuide.Helpers;

namespace ShortcutGuide.UnitTests.ActivationTests;

[TestClass]
public sealed class ManifestInterpreterTests
{
    [TestMethod]
    [DataRow("Ableton Live 12 Suite.exe")]
    [DataRow("Ableton Live 11 Standard.exe")]
    [DataRow("Live.exe")]
    public void IsMatch_SemicolonSeparatedFilters_MatchesAnyExecutable(string executableName)
    {
        Assert.IsTrue(ManifestInterpreter.IsMatch(
            executableName,
            "Ableton Live 12 Suite.exe;Ableton Live 11 Standard.exe;Live.exe"));
    }

    [TestMethod]
    public void IsMatch_SemicolonSeparatedFilters_DoesNotMatchUnlistedExecutable()
    {
        Assert.IsFalse(ManifestInterpreter.IsMatch(
            "Ableton Live 10 Intro.exe",
            "Ableton Live 12 Suite.exe;Ableton Live 11 Standard.exe;Live.exe"));
    }
}
