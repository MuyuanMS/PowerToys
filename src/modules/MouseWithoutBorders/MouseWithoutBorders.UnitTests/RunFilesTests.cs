// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.MouseWithoutBorders.UITests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MouseWithoutBorders.UnitTests;

[TestClass]
public sealed class RunFilesTests
{
    [TestMethod]
    public void RemoveEmptyRunControlParentDeletesOnlyTheRunDirectory()
    {
        var runId = Guid.NewGuid();
        var path = RunControlParent(runId);
        Directory.CreateDirectory(path);

        try
        {
            RunFiles.RemoveEmptyRunControlParent(runId.ToString("D"), TimeSpan.FromSeconds(1));

            Assert.IsFalse(Directory.Exists(path));
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    [TestMethod]
    public void RemoveEmptyRunControlParentPreservesNonemptyRunDirectory()
    {
        var runId = Guid.NewGuid();
        var path = RunControlParent(runId);
        Directory.CreateDirectory(path);
        var marker = Path.Combine(path, "owned-entry");
        File.WriteAllText(marker, "preserve");

        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                RunFiles.RemoveEmptyRunControlParent(runId.ToString("D"), TimeSpan.FromSeconds(1)));

            Assert.IsTrue(File.Exists(marker));
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private static string RunControlParent(Guid runId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "PowerToysUiTestControl",
        runId.ToString("D"));
}
