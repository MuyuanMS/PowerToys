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
    private const short ReturnVirtualKey = 0x0D;
    private const short ShiftVirtualKey = 0x10;
    private static readonly UIntPtr PowerToysInjectedTag = (UIntPtr)0x110;
    private static readonly short[] ModifierVirtualKeys = [0xA2, 0xA3, 0x5B, 0x5C, 0xA0, 0xA1, 0xA4, 0xA5];

    private readonly IUserSettings _userSettings;
    private readonly Func<IntPtr> _getForegroundWindow;
    private readonly Func<int, short> _getAsyncKeyState;
    private readonly Func<Helpers.NativeMethods.INPUT[], uint> _sendInput;
    private readonly Action<int> _delay;

    public KeystrokeService(IUserSettings userSettings)
        : this(
            userSettings,
            Helpers.NativeMethods.GetForegroundWindow,
            Helpers.NativeMethods.GetAsyncKeyState,
            inputs => Helpers.NativeMethods.SendInput((uint)inputs.Length, inputs, Helpers.NativeMethods.INPUT.Size),
            System.Threading.Thread.Sleep)
    {
    }

    internal KeystrokeService(
        IUserSettings userSettings,
        Func<IntPtr> getForegroundWindow,
        Func<Helpers.NativeMethods.INPUT[], uint> sendInput,
        Action<int> delay)
        : this(userSettings, getForegroundWindow, _ => 0, sendInput, delay)
    {
    }

    internal KeystrokeService(
        IUserSettings userSettings,
        Func<IntPtr> getForegroundWindow,
        Func<int, short> getAsyncKeyState,
        Func<Helpers.NativeMethods.INPUT[], uint> sendInput,
        Action<int> delay)
    {
        ArgumentNullException.ThrowIfNull(userSettings);
        ArgumentNullException.ThrowIfNull(getForegroundWindow);
        ArgumentNullException.ThrowIfNull(getAsyncKeyState);
        ArgumentNullException.ThrowIfNull(sendInput);
        ArgumentNullException.ThrowIfNull(delay);

        _userSettings = userSettings;
        _getForegroundWindow = getForegroundWindow;
        _getAsyncKeyState = getAsyncKeyState;
        _sendInput = sendInput;
        _delay = delay;
    }

    /// <summary>
    /// Sends text as individual Unicode keystroke events.
    /// This is useful for applications that don't support standard clipboard paste operations.
    /// </summary>
    /// <param name="text">The text to send as keystrokes.</param>
    /// <returns><see langword="true"/> when all text was sent; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when SendInput fails to send all inputs.</exception>
    public bool SendTextAsKeystrokes(string text)
    {
        Logger.LogTrace();

        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrEmpty(text))
        {
            Logger.LogWarning("Attempted to send empty text as keystrokes");
            return false;
        }

        var delayMs = _userSettings.KeystrokeDelayMs > 0 ? _userSettings.KeystrokeDelayMs : AdvancedPasteProperties.DefaultKeystrokeDelayMs;
        var batchSize = _userSettings.KeystrokeBatchSize > 0 ? _userSettings.KeystrokeBatchSize : AdvancedPasteProperties.DefaultKeystrokeBatchSize;
        var normalizedText = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var targetWindow = _getForegroundWindow();
        if (targetWindow == IntPtr.Zero)
        {
            Logger.LogWarning("Keystroke paste cancelled because there is no foreground window");
            return false;
        }

        ReleasePressedModifiers();

        for (int i = 0; i < normalizedText.Length; i++)
        {
            var currentForeground = _getForegroundWindow();
            if (currentForeground != targetWindow)
            {
                Logger.LogWarning("Keystroke paste cancelled because the foreground window changed");
                return false;
            }

            var inputs = CreateInputSequence(normalizedText[i]);
            SendInputEvents(inputs);

            if ((i + 1) % batchSize == 0 || i == normalizedText.Length - 1)
            {
                _delay(delayMs);
            }
        }

        return true;
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
                    dwExtraInfo = PowerToysInjectedTag,
                },
            },
        };
    }

    private static Helpers.NativeMethods.INPUT CreateVirtualKeyInput(short virtualKey, bool isKeyUp)
    {
        return new Helpers.NativeMethods.INPUT
        {
            type = Helpers.NativeMethods.INPUTTYPE.INPUT_KEYBOARD,
            data = new Helpers.NativeMethods.InputUnion
            {
                ki = new Helpers.NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = isKeyUp ? (uint)Helpers.NativeMethods.KeyEventF.KeyUp : 0,
                    time = 0,
                    dwExtraInfo = PowerToysInjectedTag,
                },
            },
        };
    }

    private void ReleasePressedModifiers()
    {
        var inputs = new List<Helpers.NativeMethods.INPUT>(ModifierVirtualKeys.Length);

        foreach (short modifierVirtualKey in ModifierVirtualKeys)
        {
            if ((_getAsyncKeyState(modifierVirtualKey) & 0x8000) != 0)
            {
                inputs.Add(CreateVirtualKeyInput(modifierVirtualKey, isKeyUp: true));
            }
        }

        if (inputs.Count > 0)
        {
            SendInputEvents(inputs);
        }
    }

    private List<Helpers.NativeMethods.INPUT> CreateInputSequence(char character)
    {
        if (character == '\n')
        {
            return
            [
                CreateVirtualKeyInput(ShiftVirtualKey, isKeyUp: false),
                CreateVirtualKeyInput(ReturnVirtualKey, isKeyUp: false),
                CreateVirtualKeyInput(ReturnVirtualKey, isKeyUp: true),
                CreateVirtualKeyInput(ShiftVirtualKey, isKeyUp: true),
            ];
        }

        var inputs = new List<Helpers.NativeMethods.INPUT>(2)
        {
            CreateUnicodeInput(character, isKeyUp: false),
            CreateUnicodeInput(character, isKeyUp: true),
        };

        return inputs;
    }

    private void SendInputEvents(List<Helpers.NativeMethods.INPUT> inputs)
    {
        uint sent = _sendInput(inputs.ToArray());

        if (sent != inputs.Count)
        {
            var errorMessage = $"SendInput failed: only {sent} of {inputs.Count} inputs were sent";
            Logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }
    }
}
