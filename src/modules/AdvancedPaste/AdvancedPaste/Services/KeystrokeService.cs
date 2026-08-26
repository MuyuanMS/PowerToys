// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;

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
    /// <param name="cancellationToken">Token used to stop an in-progress keystroke paste.</param>
    /// <returns><see langword="true"/> when all text was sent; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when SendInput fails to send all inputs.</exception>
    public bool SendTextAsKeystrokes(string text, CancellationToken cancellationToken = default)
    {
        Logger.LogTrace();

        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrEmpty(text))
        {
            Logger.LogWarning("Attempted to send empty text as keystrokes");
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
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
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

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
            ReleasePartiallyPressedKeys(inputs, sent);

            var errorMessage = $"SendInput failed: only {sent} of {inputs.Count} inputs were sent";
            Logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }
    }

    private void ReleasePartiallyPressedKeys(List<Helpers.NativeMethods.INPUT> inputs, uint sent)
    {
        var pressedKeys = new List<Helpers.NativeMethods.INPUT>();
        var acceptedCount = Math.Min((int)sent, inputs.Count);

        for (int i = 0; i < acceptedCount; i++)
        {
            var input = inputs[i];
            if ((input.data.ki.dwFlags & (uint)Helpers.NativeMethods.KeyEventF.KeyUp) == 0)
            {
                pressedKeys.Add(input);
                continue;
            }

            var matchingKeyDown = pressedKeys.FindLastIndex(keyDown =>
                keyDown.data.ki.wVk == input.data.ki.wVk &&
                keyDown.data.ki.wScan == input.data.ki.wScan &&
                (keyDown.data.ki.dwFlags & (uint)Helpers.NativeMethods.KeyEventF.Unicode) ==
                (input.data.ki.dwFlags & (uint)Helpers.NativeMethods.KeyEventF.Unicode));

            if (matchingKeyDown >= 0)
            {
                pressedKeys.RemoveAt(matchingKeyDown);
            }
        }

        if (pressedKeys.Count == 0)
        {
            return;
        }

        pressedKeys.Reverse();
        for (int i = 0; i < pressedKeys.Count; i++)
        {
            var cleanupInput = pressedKeys[i];
            var keyboardInput = cleanupInput.data.ki;
            keyboardInput.dwFlags |= (uint)Helpers.NativeMethods.KeyEventF.KeyUp;
            cleanupInput.data.ki = keyboardInput;
            pressedKeys[i] = cleanupInput;
        }

        var cleanupSent = _sendInput(pressedKeys.ToArray());
        if (cleanupSent != pressedKeys.Count)
        {
            Logger.LogError($"SendInput cleanup failed: only {cleanupSent} of {pressedKeys.Count} key-up inputs were sent");
        }
    }
}
