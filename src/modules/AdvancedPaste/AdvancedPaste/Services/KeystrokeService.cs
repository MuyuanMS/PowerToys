// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

using AdvancedPaste.Settings;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;

namespace AdvancedPaste.Services;

/// <summary>
/// Service for sending text as keyboard input events.
/// </summary>
public sealed class KeystrokeService : IKeystrokeService
{
    private readonly IUserSettings _userSettings;
    private readonly Func<IntPtr> _getForegroundWindow;
    private readonly Func<Helpers.NativeMethods.INPUT[], uint> _sendInput;
    private readonly Action<int> _delay;

    public KeystrokeService(IUserSettings userSettings)
        : this(
            userSettings,
            Helpers.NativeMethods.GetForegroundWindow,
            inputs => Helpers.NativeMethods.SendInput((uint)inputs.Length, inputs, Helpers.NativeMethods.INPUT.Size),
            System.Threading.Thread.Sleep)
    {
    }

    internal KeystrokeService(
        IUserSettings userSettings,
        Func<IntPtr> getForegroundWindow,
        Func<Helpers.NativeMethods.INPUT[], uint> sendInput,
        Action<int> delay)
    {
        ArgumentNullException.ThrowIfNull(userSettings);
        ArgumentNullException.ThrowIfNull(getForegroundWindow);
        ArgumentNullException.ThrowIfNull(sendInput);
        ArgumentNullException.ThrowIfNull(delay);

        _userSettings = userSettings;
        _getForegroundWindow = getForegroundWindow;
        _sendInput = sendInput;
        _delay = delay;
    }

    /// <summary>
    /// Sends text as individual Unicode keystroke events.
    /// This is useful for applications that don't support standard clipboard paste operations.
    /// </summary>
    /// <param name="text">The text to send as keystrokes.</param>
    /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when SendInput fails to send all inputs.</exception>
    public void SendTextAsKeystrokes(string text)
    {
        Logger.LogTrace();

        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrEmpty(text))
        {
            Logger.LogWarning("Attempted to send empty text as keystrokes");
            return;
        }

        var delayMs = _userSettings.KeystrokeDelayMs > 0 ? _userSettings.KeystrokeDelayMs : AdvancedPasteProperties.DefaultKeystrokeDelayMs;
        var batchSize = _userSettings.KeystrokeBatchSize > 0 ? _userSettings.KeystrokeBatchSize : AdvancedPasteProperties.DefaultKeystrokeBatchSize;
        var targetWindow = _getForegroundWindow();
        if (targetWindow == IntPtr.Zero)
        {
            Logger.LogWarning("Keystroke paste cancelled because there is no foreground window");
            return;
        }

        for (int i = 0; i < text.Length; i += batchSize)
        {
            var currentForeground = _getForegroundWindow();
            if (currentForeground != targetWindow)
            {
                Logger.LogWarning("Keystroke paste cancelled because the foreground window changed");
                break;
            }

            int currentChunkLength = Math.Min(batchSize, text.Length - i);

            if (currentChunkLength > 0)
            {
                var inputs = CreateInputSequence(text.AsSpan(i, currentChunkLength));
                SendInputEvents(inputs, delayMs);
            }
        }
    }

    private static Helpers.NativeMethods.INPUT CreateUnicodeInput(char character, bool isKeyUp)
    {
        return new Helpers.NativeMethods.INPUT
        {
            type = Helpers.NativeMethods.INPUTTYPE.INPUT_KEYBOARD,
            data = new Helpers.NativeMethods.InputUnion
            {
                ki = new Helpers.NativeMethods.KEYBDINPUT
                {
                    wVk = 0,  // Must be 0 for Unicode input
                    wScan = (short)character,
                    dwFlags = (uint)Helpers.NativeMethods.KeyEventF.Unicode |
                              (isKeyUp ? (uint)Helpers.NativeMethods.KeyEventF.KeyUp : 0),
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero,
                },
            },
        };
    }

    private List<Helpers.NativeMethods.INPUT> CreateInputSequence(ReadOnlySpan<char> text)
    {
        var inputs = new List<Helpers.NativeMethods.INPUT>(text.Length * 2);

        foreach (char c in text)
        {
            inputs.Add(CreateUnicodeInput(c, isKeyUp: false));

            inputs.Add(CreateUnicodeInput(c, isKeyUp: true));
        }

        return inputs;
    }

    private void SendInputEvents(List<Helpers.NativeMethods.INPUT> inputs, int delayMs)
    {
        uint sent = _sendInput(inputs.ToArray());

        if (sent != inputs.Count)
        {
            var errorMessage = $"SendInput failed: only {sent} of {inputs.Count} inputs were sent";
            Logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        // SendInput cannot handle rapid sequences of inputs. Delay is configurable.
        _delay(delayMs);
    }
}
