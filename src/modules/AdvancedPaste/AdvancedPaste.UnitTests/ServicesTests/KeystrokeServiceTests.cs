// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedPaste.Helpers;
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

        public string FixSpellingAndGrammarPrompt => string.Empty;

        public string FixSpellingAndGrammarSystemPrompt => string.Empty;

        public string FixSpellingAndGrammarProviderId => string.Empty;

        public bool FixSpellingAndGrammarCoachingEnabled => false;

        public bool FixSpellingAndGrammarCoachingShortcutSet => false;

        public string FixSpellingAndGrammarCoachingPrompt => string.Empty;

        public string FixSpellingAndGrammarCoachingSystemPrompt => string.Empty;

        public string FixSpellingAndGrammarCoachingProviderId => string.Empty;

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

        var completed = service.SendTextAsKeystrokes("abcde");

        Assert.IsTrue(completed);
        Assert.AreEqual(5, inputCounts.Count);
        Assert.IsTrue(inputCounts.TrueForAll(inputCount => inputCount == 2));
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

        var completed = service.SendTextAsKeystrokes("abc");

        Assert.IsTrue(completed);
        Assert.AreEqual(3, inputCounts.Count);
        Assert.IsTrue(inputCounts.TrueForAll(inputCount => inputCount == 2));
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

        var completed = service.SendTextAsKeystrokes("ab");

        Assert.IsTrue(completed);
        Assert.AreEqual(2, inputCounts.Count);
        Assert.IsTrue(inputCounts.TrueForAll(inputCount => inputCount == 2));
        Assert.AreEqual(2, delays.Count);
        Assert.IsTrue(delays.TrueForAll(delay => delay == AdvancedPasteProperties.DefaultKeystrokeDelayMs));
    }

    [TestMethod]
    public void SendTextAsKeystrokes_WithoutForegroundWindowDoesNotSendInput()
    {
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 50,
            KeystrokeBatchSize = 2,
        };
        var sendCount = 0;
        var service = new KeystrokeService(
            userSettings,
            () => IntPtr.Zero,
            inputs =>
            {
                sendCount++;
                return (uint)inputs.Length;
            },
            _ => { });

        var completed = service.SendTextAsKeystrokes("abc");

        Assert.IsFalse(completed);
        Assert.AreEqual(0, sendCount);
    }

    [TestMethod]
    public void SendTextAsKeystrokes_MultilineTextUsesReturnKey()
    {
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 50,
            KeystrokeBatchSize = 10,
        };
        var sentBatches = new List<NativeMethods.INPUT[]>();
        var service = new KeystrokeService(
            userSettings,
            () => new IntPtr(1),
            inputs =>
            {
                sentBatches.Add(inputs);
                return (uint)inputs.Length;
            },
            _ => { });

        var completed = service.SendTextAsKeystrokes("a\r\nb\nc");

        Assert.IsTrue(completed);
        Assert.AreEqual(5, sentBatches.Count);
        Assert.AreEqual(4, sentBatches[1].Length);
        Assert.AreEqual((short)0x10, sentBatches[1][0].data.ki.wVk);
        Assert.AreEqual((short)0x0D, sentBatches[1][1].data.ki.wVk);
        Assert.AreEqual((uint)NativeMethods.KeyEventF.KeyUp, sentBatches[1][2].data.ki.dwFlags);
        Assert.AreEqual((uint)NativeMethods.KeyEventF.KeyUp, sentBatches[1][3].data.ki.dwFlags);
        Assert.IsTrue(sentBatches[1].All(input => input.data.ki.dwExtraInfo == (UIntPtr)0x110));
        Assert.AreEqual(4, sentBatches[3].Length);
    }

    [TestMethod]
    public void SendTextAsKeystrokes_ReleasesPressedModifiersBeforeTyping()
    {
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 50,
            KeystrokeBatchSize = 1,
        };
        var sentBatches = new List<NativeMethods.INPUT[]>();
        var service = new KeystrokeService(
            userSettings,
            () => new IntPtr(1),
            virtualKey => virtualKey == 0xA2 ? unchecked((short)0x8000) : (short)0,
            inputs =>
            {
                sentBatches.Add(inputs);
                return (uint)inputs.Length;
            },
            _ => { });

        var completed = service.SendTextAsKeystrokes("a");

        Assert.IsTrue(completed);
        Assert.AreEqual(2, sentBatches.Count);
        Assert.AreEqual((short)0xA2, sentBatches[0][0].data.ki.wVk);
        Assert.AreEqual((uint)NativeMethods.KeyEventF.KeyUp, sentBatches[0][0].data.ki.dwFlags);
        Assert.AreEqual((UIntPtr)0x110, sentBatches[0][0].data.ki.dwExtraInfo);
        Assert.AreEqual((short)'a', sentBatches[1][0].data.ki.wScan);
    }

    [TestMethod]
    public void SendTextAsKeystrokes_ForegroundWindowChangeReturnsFalse()
    {
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 50,
            KeystrokeBatchSize = 1,
        };
        var foregroundChecks = 0;
        var sendCount = 0;
        var service = new KeystrokeService(
            userSettings,
            () => ++foregroundChecks < 3 ? new IntPtr(1) : new IntPtr(2),
            inputs =>
            {
                sendCount++;
                return (uint)inputs.Length;
            },
            _ => { });

        var completed = service.SendTextAsKeystrokes("ab");

        Assert.IsFalse(completed);
        Assert.AreEqual(1, sendCount);
    }

    [TestMethod]
    public void SendTextAsKeystrokes_PartialNewlineInputReleasesPressedKeys()
    {
        var userSettings = new TestUserSettings
        {
            KeystrokeDelayMs = 50,
            KeystrokeBatchSize = 1,
        };
        var sentBatches = new List<NativeMethods.INPUT[]>();
        var service = new KeystrokeService(
            userSettings,
            () => new IntPtr(1),
            inputs =>
            {
                sentBatches.Add(inputs);
                return sentBatches.Count == 1 ? 2U : (uint)inputs.Length;
            },
            _ => { });

        try
        {
            service.SendTextAsKeystrokes("\n");
            Assert.Fail("Expected a partial SendInput result to throw.");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.AreEqual(2, sentBatches.Count);
        Assert.AreEqual(2, sentBatches[1].Length);
        Assert.AreEqual((short)0x0D, sentBatches[1][0].data.ki.wVk);
        Assert.AreEqual((uint)NativeMethods.KeyEventF.KeyUp, sentBatches[1][0].data.ki.dwFlags);
        Assert.AreEqual((short)0x10, sentBatches[1][1].data.ki.wVk);
        Assert.AreEqual((uint)NativeMethods.KeyEventF.KeyUp, sentBatches[1][1].data.ki.dwFlags);
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
