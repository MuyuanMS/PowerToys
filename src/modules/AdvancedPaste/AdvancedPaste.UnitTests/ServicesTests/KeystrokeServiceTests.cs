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

        public bool CloseAfterLosingFocus { get; set; }

        public bool EnableClipboardPreview { get; set; }

        public bool ShowAIPaste { get; set; }

        public int KeystrokeDelayMs { get; set; }

        public int KeystrokeBatchSize { get; set; }

        public IReadOnlyList<AdvancedPasteCustomAction> CustomActions => Array.Empty<AdvancedPasteCustomAction>();

        public IReadOnlyList<PasteFormats> AdditionalActions => Array.Empty<PasteFormats>();

        public PasteAIConfiguration PasteAIConfiguration { get; set; } = new();

        public event EventHandler Changed
        {
            add
            {
            }

            remove
            {
            }
        }

        public System.Threading.Tasks.Task SetActiveAIProviderAsync(string providerId)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    [TestMethod]
    public void SendTextAsKeystrokes_UsesConfiguredBatchSizeAndDelay()
    {
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 50,
            KeystrokeBatchSize = 2,
        };
        var inputCounts = new List<int>();
        var delays = new List<int>();
        var service = CreateService(userSettings, inputCounts, delays);

        service.SendTextAsKeystrokes("abcde");

        Assert.AreEqual(3, inputCounts.Count);
        Assert.AreEqual(4, inputCounts[0]);
        Assert.AreEqual(4, inputCounts[1]);
        Assert.AreEqual(2, inputCounts[2]);
        Assert.AreEqual(3, delays.Count);
        Assert.IsTrue(delays.TrueForAll(delay => delay == 50));
    }

    [TestMethod]
    public void SendTextAsKeystrokes_UsesUpdatedSettings()
    {
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 10,
            KeystrokeBatchSize = 1,
        };
        var inputCounts = new List<int>();
        var delays = new List<int>();
        var service = CreateService(userSettings, inputCounts, delays);
        userSettings.KeystrokeDelayMs = 20;
        userSettings.KeystrokeBatchSize = 3;

        service.SendTextAsKeystrokes("abc");

        Assert.AreEqual(1, inputCounts.Count);
        Assert.AreEqual(6, inputCounts[0]);
        Assert.AreEqual(1, delays.Count);
        Assert.AreEqual(20, delays[0]);
    }

    [TestMethod]
    public void SendTextAsKeystrokes_InvalidSettingsUseDefaults()
    {
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 0,
            KeystrokeBatchSize = -1,
        };
        var inputCounts = new List<int>();
        var delays = new List<int>();
        var service = CreateService(userSettings, inputCounts, delays);

        service.SendTextAsKeystrokes("ab");

        Assert.AreEqual(2, inputCounts.Count);
        Assert.IsTrue(inputCounts.TrueForAll(inputCount => inputCount == 2));
        Assert.AreEqual(2, delays.Count);
        Assert.IsTrue(delays.TrueForAll(delay => delay == AdvancedPasteProperties.DefaultKeystrokeDelayMs));
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Act
        var service = new KeystrokeService(null);

        // Assert - Exception should be thrown
    }

    private static KeystrokeService CreateService(TestUserSettings userSettings, List<int> inputCounts, List<int> delays)
    {
        return new KeystrokeService(
            userSettings,
            () => new IntPtr(1),
            inputs =>
            {
                inputCounts.Add(inputs.Length);
                return (uint)inputs.Length;
            },
            delays.Add);
    }
}
