// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;

using AdvancedPaste.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace AdvancedPaste.Cli.UnitTests;

[TestClass]
public class AdvancedPasteTempFileManagerTests
{
    private string _testTempDirectory = string.Empty;

    [TestInitialize]
    public void TestInitialize()
    {
        _testTempDirectory = Directory.CreateTempSubdirectory("PowerToys_AdvancedPaste_Test_").FullName;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_testTempDirectory))
        {
            Directory.Delete(_testTempDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void CleanupStaleDirectories_PreservesUnownedDirectories()
    {
        var directoryPath = Path.Combine(_testTempDirectory, $"PowerToys_AdvancedPaste_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        var userFilePath = Path.Combine(directoryPath, "user-data.txt");
        File.WriteAllText(userFilePath, "preserve");
        Directory.SetCreationTimeUtc(directoryPath, DateTime.UtcNow.AddDays(-2));

        AdvancedPasteTempFileManager.CleanupStaleDirectories(TimeSpan.FromDays(1), [], _testTempDirectory);

        Assert.IsTrue(File.Exists(userFilePath));
        Assert.AreEqual("preserve", File.ReadAllText(userFilePath));
    }

    [TestMethod]
    public void CleanupStaleDirectories_RemovesOwnedDirectories()
    {
        var directory = AdvancedPasteTempFileManager.CreateDirectory(_testTempDirectory);
        File.WriteAllText(Path.Combine(directory.FullName, "stale.txt"), "stale");
        Directory.SetCreationTimeUtc(directory.FullName, DateTime.UtcNow.AddDays(-2));

        AdvancedPasteTempFileManager.CleanupStaleDirectories(TimeSpan.FromDays(1), [], _testTempDirectory);

        Assert.IsFalse(Directory.Exists(directory.FullName));
    }

    [TestMethod]
    public void CleanupStaleDirectories_PreservesOwnershipMarkerUntilDirectoryIsEmpty()
    {
        var directory = AdvancedPasteTempFileManager.CreateDirectory(_testTempDirectory);
        var nestedDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "nested"));
        File.WriteAllText(Path.Combine(nestedDirectory.FullName, "stale.txt"), "stale");
        Directory.SetCreationTimeUtc(directory.FullName, DateTime.UtcNow.AddDays(-2));

        AdvancedPasteTempFileManager.CleanupStaleDirectories(TimeSpan.FromDays(1), [], _testTempDirectory);

        Assert.IsTrue(File.Exists(Path.Combine(directory.FullName, ".powertoys-advanced-paste-owned")));
        Assert.IsTrue(Directory.Exists(directory.FullName));

        Directory.Delete(nestedDirectory.FullName, recursive: true);
        AdvancedPasteTempFileManager.CleanupStaleDirectories(TimeSpan.FromDays(1), [], _testTempDirectory);

        Assert.IsFalse(Directory.Exists(directory.FullName));
    }

    [TestMethod]
    public async Task TryCleanupAfterDelayAsync_RemovesOwnershipMarkerFromConsumedFileDirectory()
    {
        var directory = AdvancedPasteTempFileManager.CreateDirectory(_testTempDirectory);
        var filePath = Path.Combine(directory.FullName, "result.txt");
        File.WriteAllText(filePath, "generated");
        var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
        var package = new DataPackage();
        package.SetStorageItems([storageFile]);

        await package.GetView().TryCleanupAfterDelayAsync(TimeSpan.Zero);

        Assert.IsFalse(Directory.Exists(directory.FullName));
    }

    [TestMethod]
    public void CleanupStaleDirectories_PreservesDirectoriesReferencedByClipboard()
    {
        var directory = AdvancedPasteTempFileManager.CreateDirectory(_testTempDirectory);
        var filePath = Path.Combine(directory.FullName, "clipboard-output.txt");
        File.WriteAllText(filePath, "keep");
        Directory.SetCreationTimeUtc(directory.FullName, DateTime.UtcNow.AddDays(-2));

        AdvancedPasteTempFileManager.CleanupStaleDirectories(TimeSpan.FromDays(1), [filePath], _testTempDirectory);

        Assert.IsTrue(File.Exists(filePath));
        Assert.IsTrue(Directory.Exists(directory.FullName));

        AdvancedPasteTempFileManager.CleanupStaleDirectories(TimeSpan.FromDays(1), [], _testTempDirectory);
        Assert.IsFalse(Directory.Exists(directory.FullName));
    }
}
