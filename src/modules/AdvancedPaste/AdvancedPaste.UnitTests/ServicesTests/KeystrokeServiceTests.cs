// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using AdvancedPaste.Models;
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

        public bool ShowAIPaste { get; set; }

        public bool CloseAfterLosingFocus { get; set; }

        public bool EnableClipboardPreview { get; set; }

        public int KeystrokeDelayMs { get; set; }

        public int KeystrokeBatchSize { get; set; }

        public IReadOnlyList<AdvancedPasteCustomAction> CustomActions => Array.Empty<AdvancedPasteCustomAction>();

        public IReadOnlyList<PasteFormats> AdditionalActions => Array.Empty<PasteFormats>();

        public PasteAIConfiguration PasteAIConfiguration { get; set; } = new();

#pragma warning disable CS0067 // Event is never used (required by interface)
        public event EventHandler Changed;
#pragma warning restore CS0067

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

    [TestMethod]
    public void GetChunkLength_NormalText_ReturnsBatchSize()
    {
        // "Hello" with batch size 2 at index 0 → chunk of 2
        Assert.AreEqual(2, KeystrokeService.GetChunkLength("Hello", 0, 2));
    }

    [TestMethod]
    public void GetChunkLength_AtEnd_ReturnsRemaining()
    {
        // "Hello" with batch size 3 at index 3 → only 2 remaining
        Assert.AreEqual(2, KeystrokeService.GetChunkLength("Hello", 3, 3));
    }

    [TestMethod]
    public void GetChunkLength_SurrogatePair_ExtendsChunk()
    {
        // 🎉 = \uD83C\uDF89 (surrogate pair). With batch 1 at index 0, chunk must include both chars
        string text = "\uD83C\uDF89X";
        Assert.AreEqual(2, KeystrokeService.GetChunkLength(text, 0, 1));
    }

    [TestMethod]
    public void GetChunkLength_CrLf_ExtendsChunk()
    {
        // \r\n with batch size 1 at index where chunk ends on \r → extends to include \n
        string text = "A\r\nB";

        // At index 1, batch 1 → would be just \r, but extends to \r\n
        Assert.AreEqual(2, KeystrokeService.GetChunkLength(text, 1, 1));
    }

    [TestMethod]
    public void GetChunkLength_LoneCr_DoesNotExtend()
    {
        // \r not followed by \n should not extend
        string text = "A\rB";
        Assert.AreEqual(1, KeystrokeService.GetChunkLength(text, 1, 1));
    }

    [TestMethod]
    public void IsSkippableCarriageReturn_CrLf_ReturnsTrue()
    {
        ReadOnlySpan<char> text = "\r\n".AsSpan();
        Assert.IsTrue(KeystrokeService.IsSkippableCarriageReturn(text, 0));
    }

    [TestMethod]
    public void IsSkippableCarriageReturn_LoneCr_ReturnsFalse()
    {
        ReadOnlySpan<char> text = "\rX".AsSpan();
        Assert.IsFalse(KeystrokeService.IsSkippableCarriageReturn(text, 0));
    }

    [TestMethod]
    public void IsSkippableCarriageReturn_NotCr_ReturnsFalse()
    {
        ReadOnlySpan<char> text = "AB".AsSpan();
        Assert.IsFalse(KeystrokeService.IsSkippableCarriageReturn(text, 0));
    }
}
