// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using AdvancedPaste.Services;
using AdvancedPaste.Settings;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedPaste.UnitTests.ServicesTests;

[TestClass]
public sealed class KeystrokeServiceTests
{
    private sealed class TestUserSettings : IUserSettings
    {
        public bool IsAIEnabled { get; set; }

        public bool ShowCustomPreview { get; set; }

        public bool CloseAfterLosingFocus { get; set; }

        public bool EnableClipboardPreview { get; set; }

        public int KeystrokeDelayMs { get; set; }

        public int KeystrokeBatchSize { get; set; }

        public IReadOnlyList<AdvancedPasteCustomAction> CustomActions => Array.Empty<AdvancedPasteCustomAction>();

        public IReadOnlyList<PasteFormats> AdditionalActions => Array.Empty<PasteFormats>();

        public PasteAIConfiguration PasteAIConfiguration { get; set; } = new();

        public event EventHandler Changed;

        public System.Threading.Tasks.Task SetActiveAIProviderAsync(string providerId)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    [TestMethod]
    public void Constructor_WithValidSettings_UsesConfiguredValues()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 50,
            KeystrokeBatchSize = 5,
        };

        // Act
        var service = new KeystrokeService(userSettings);

        // Assert
        Assert.AreEqual(50, service.EffectiveDelayMs);
        Assert.AreEqual(5, service.EffectiveBatchSize);
    }

    [TestMethod]
    public void Constructor_WithZeroDelay_FallsBackToDefault()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 0,
            KeystrokeBatchSize = 2,
        };

        // Act
        var service = new KeystrokeService(userSettings);

        // Assert
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, service.EffectiveDelayMs);
        Assert.AreEqual(2, service.EffectiveBatchSize);
    }

    [TestMethod]
    public void Constructor_WithNegativeDelay_FallsBackToDefault()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = -10,
            KeystrokeBatchSize = 3,
        };

        // Act
        var service = new KeystrokeService(userSettings);

        // Assert
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, service.EffectiveDelayMs);
        Assert.AreEqual(3, service.EffectiveBatchSize);
    }

    [TestMethod]
    public void Constructor_WithZeroBatchSize_FallsBackToDefault()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 25,
            KeystrokeBatchSize = 0,
        };

        // Act
        var service = new KeystrokeService(userSettings);

        // Assert
        Assert.AreEqual(25, service.EffectiveDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, service.EffectiveBatchSize);
    }

    [TestMethod]
    public void Constructor_WithNegativeBatchSize_FallsBackToDefault()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 25,
            KeystrokeBatchSize = -5,
        };

        // Act
        var service = new KeystrokeService(userSettings);

        // Assert
        Assert.AreEqual(25, service.EffectiveDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, service.EffectiveBatchSize);
    }

    [TestMethod]
    public void Constructor_WithDefaultValues_CreatesService()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = AdvancedPasteProperties.DefaultKeystrokeDelayMs,
            KeystrokeBatchSize = AdvancedPasteProperties.DefaultKeystrokeBatchSize,
        };

        // Act
        var service = new KeystrokeService(userSettings);

        // Assert
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeDelayMs, service.EffectiveDelayMs);
        Assert.AreEqual(AdvancedPasteProperties.DefaultKeystrokeBatchSize, service.EffectiveBatchSize);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Act
        var service = new KeystrokeService(null);
    }

    [TestMethod]
    public void Constructor_WithLargeDelay_UsesProvidedValue()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 1000,
            KeystrokeBatchSize = 1,
        };

        // Act
        var service = new KeystrokeService(userSettings);

        // Assert
        Assert.AreEqual(1000, service.EffectiveDelayMs);
    }

    [TestMethod]
    public void Constructor_WithLargeBatchSize_UsesProvidedValue()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 30,
            KeystrokeBatchSize = 100,
        };

        // Act
        var service = new KeystrokeService(userSettings);

        // Assert
        Assert.AreEqual(100, service.EffectiveBatchSize);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void SendTextAsKeystrokes_WithNullText_ThrowsArgumentNullException()
    {
        // Arrange
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 30,
            KeystrokeBatchSize = 1,
        };
        var service = new KeystrokeService(userSettings);

        // Act
        service.SendTextAsKeystrokes(null);
    }
}
