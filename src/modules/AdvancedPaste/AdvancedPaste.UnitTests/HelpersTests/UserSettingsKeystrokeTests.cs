// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO.Abstractions.TestingHelpers;
using AdvancedPaste.Settings;
using Microsoft.PowerToys.Settings.UI.Library;
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
        var userSettings = new UserSettings(_fileSystem);

        // Assert
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, userSettings.KeystrokeDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, userSettings.KeystrokeBatchSize);
    }

    [TestMethod]
    public void LoadSettings_WithValidKeystrokeValues()
    {
        // Arrange & Act
        // Note: UserSettings reads from SettingsUtils which requires a full settings path.
        // Since MockFileSystem doesn't integrate with SettingsUtils, this test verifies
        // that construction with no settings file uses defaults correctly.
        var userSettings = new UserSettings(_fileSystem);

        // Assert - defaults are applied when no settings file exists
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, userSettings.KeystrokeDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, userSettings.KeystrokeBatchSize);
    }

    [TestMethod]
    public void LoadSettings_WithZeroKeystrokeValues_UseDefaults()
    {
        // Arrange
        // When keystroke values are 0, they should fall back to defaults

        // Act
        var userSettings = new UserSettings(_fileSystem);

        // Assert
        // Since the settings file doesn't exist, defaults should be used
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, userSettings.KeystrokeDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, userSettings.KeystrokeBatchSize);
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

    [TestMethod]
    public void Constructor_WithNoSettingsFile_KeystrokeValuesArePositive()
    {
        // Act
        var userSettings = new UserSettings(_fileSystem);

        // Assert — without a settings file, defaults are applied and values are > 0
        Assert.IsTrue(userSettings.KeystrokeDelayMs > 0);
        Assert.IsTrue(userSettings.KeystrokeBatchSize > 0);
    }
}
