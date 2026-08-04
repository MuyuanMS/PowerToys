// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using AdvancedPaste.Settings;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedPaste.UnitTests.HelpersTests;

[TestClass]
public sealed class UserSettingsKeystrokeTests
{
    private MockFileSystem _fileSystem;

    [TestInitialize]
    public void Setup()
    {
        _fileSystem = new MockFileSystem();
    }

    [TestMethod]
    public void Constructor_InitializesWithDefaultKeystrokeValues()
    {
        // Act
        using var userSettings = new UserSettings(_fileSystem, TaskScheduler.Default);

        // Assert
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, userSettings.KeystrokeDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, userSettings.KeystrokeBatchSize);
    }

    [TestMethod]
    public void LoadSettings_WithValidKeystrokeValues()
    {
        using var userSettings = CreateUserSettings(50, 5);

        Assert.AreEqual(50, userSettings.KeystrokeDelayMs);
        Assert.AreEqual(5, userSettings.KeystrokeBatchSize);
    }

    [TestMethod]
    public void LoadSettings_WithZeroKeystrokeValues_UseDefaults()
    {
        using var userSettings = CreateUserSettings(0, 0);

        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, userSettings.KeystrokeDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, userSettings.KeystrokeBatchSize);
    }

    [TestMethod]
    public void LoadSettings_WithNegativeKeystrokeValues_UseDefaults()
    {
        using var userSettings = CreateUserSettings(-10, -5);

        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, userSettings.KeystrokeDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, userSettings.KeystrokeBatchSize);
    }

    [TestMethod]
    public void Constructor_WithLargeKeystrokeValues()
    {
        using var userSettings = CreateUserSettings(1000, 100);

        Assert.AreEqual(1000, userSettings.KeystrokeDelayMs);
        Assert.AreEqual(100, userSettings.KeystrokeBatchSize);
    }

    [TestMethod]
    public void DefaultKeystrokeDelayMs_Is30Milliseconds()
    {
        // Assert
        Assert.AreEqual(30, AdvancedPasteProperties.DefaultKeystrokeDelayMs);
    }

    [TestMethod]
    public void DefaultKeystrokeBatchSize_Is1()
    {
        // Assert
        Assert.AreEqual(1, AdvancedPasteProperties.DefaultKeystrokeBatchSize);
    }

    private UserSettings CreateUserSettings(int delayMs, int batchSize)
    {
        var settings = new AdvancedPasteSettings();
        settings.Properties.KeystrokeDelayMs = delayMs;
        settings.Properties.KeystrokeBatchSize = batchSize;

        var settingsPath = _fileSystem.Path.Combine(
            Helper.LocalApplicationDataFolder(),
            @"Microsoft\PowerToys\AdvancedPaste\settings.json");
        _fileSystem.AddFile(settingsPath, new MockFileData(settings.ToJsonString()));
        return new UserSettings(_fileSystem, TaskScheduler.Default);
    }
}
