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
    private readonly IUserSettings _userSettings;

    public KeystrokeService(IUserSettings userSettings)
    {
        ArgumentNullException.ThrowIfNull(userSettings);
        _userSettings = userSettings;
    }

    // Exposed for unit testing — reads live from settings each time
    internal int EffectiveDelayMs => _userSettings.KeystrokeDelayMs > 0
        ? _userSettings.KeystrokeDelayMs
        : AdvancedPasteProperties.DefaultKeystrokeDelayMs;

    internal int EffectiveBatchSize => _userSettings.KeystrokeBatchSize > 0
        ? _userSettings.KeystrokeBatchSize
        : AdvancedPasteProperties.DefaultKeystrokeBatchSize;

    /// <summary>
    /// Sends text as individual Unicode keystroke events.
    /// This is useful for applications that don't support standard clipboard paste operations.
    /// </summary>
    /// <param name="text">The text to send as keystrokes.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown when SendInput fails to send all inputs.</exception>
    public void SendTextAsKeystrokes(string text, CancellationToken cancellationToken = default)
    {
        Logger.LogTrace();

        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrEmpty(text))
        {
            Logger.LogWarning("Attempted to send empty text as keystrokes");
            return;
        }

        var targetWindow = Helpers.NativeMethods.GetForegroundWindow();

        for (int i = 0; i < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentForeground = Helpers.NativeMethods.GetForegroundWindow();
            if (targetWindow != IntPtr.Zero && currentForeground != targetWindow)
            {
                Logger.LogWarning("Keystroke paste cancelled because the foreground window changed");
                break;
            }

            int currentChunkLength = GetChunkLength(text, i, EffectiveBatchSize);

            if (currentChunkLength > 0)
            {
                var inputs = CreateInputSequence(text.AsSpan(i, currentChunkLength));
                SendInputEvents(inputs);
            }

            i += currentChunkLength;
        }
    }

    /// <summary>
    /// Computes the chunk length at position <paramref name="startIndex"/>, ensuring surrogate pairs
    /// and \r\n sequences are never split across batches.
    /// </summary>
    internal static int GetChunkLength(string text, int startIndex, int batchSize)
    {
        int remaining = text.Length - startIndex;
        int chunkLength = Math.Min(batchSize, remaining);

        // Avoid splitting a surrogate pair across batches
        if (chunkLength < remaining && char.IsHighSurrogate(text[startIndex + chunkLength - 1]))
        {
            chunkLength++;
        }

        // Avoid splitting \r\n across batches so it collapses to a single Enter
        if (chunkLength < remaining && text[startIndex + chunkLength - 1] == '\r' && text[startIndex + chunkLength] == '\n')
        {
            chunkLength++;
        }

        return chunkLength;
    }

    /// <summary>
    /// Returns true if the \r at position <paramref name="i"/> should be skipped (followed by \n).
    /// </summary>
    internal static bool IsSkippableCarriageReturn(ReadOnlySpan<char> text, int i)
        => text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n';

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

    private static Helpers.NativeMethods.INPUT CreateVirtualKeyInput(short vk, bool isKeyUp)
    {
        return new Helpers.NativeMethods.INPUT
        {
            type = Helpers.NativeMethods.INPUTTYPE.INPUT_KEYBOARD,
            data = new Helpers.NativeMethods.InputUnion
            {
                ki = new Helpers.NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = isKeyUp ? (uint)Helpers.NativeMethods.KeyEventF.KeyUp : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero,
                },
            },
        };
    }

    private List<Helpers.NativeMethods.INPUT> CreateInputSequence(ReadOnlySpan<char> text)
    {
        var inputs = new List<Helpers.NativeMethods.INPUT>(text.Length * 2);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Skip \r when followed by \n (handle \r\n as a single Enter)
            if (IsSkippableCarriageReturn(text, i))
            {
                continue;
            }

            // Use VK_RETURN for newline characters instead of Unicode input
            if (c == '\n' || c == '\r')
            {
                const short VK_RETURN = 0x0D;
                inputs.Add(CreateVirtualKeyInput(VK_RETURN, isKeyUp: false));
                inputs.Add(CreateVirtualKeyInput(VK_RETURN, isKeyUp: true));
            }
            else
            {
                inputs.Add(CreateUnicodeInput(c, isKeyUp: false));
                inputs.Add(CreateUnicodeInput(c, isKeyUp: true));
            }
        }

        return inputs;
    }

    private void SendInputEvents(List<Helpers.NativeMethods.INPUT> inputs)
    {
        uint sent = Helpers.NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Helpers.NativeMethods.INPUT.Size);

        if (sent != inputs.Count)
        {
            var errorMessage = $"SendInput failed: only {sent} of {inputs.Count} inputs were sent";
            Logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        // SendInput cannot handle rapid sequences of inputs. Delay is configurable.
        System.Threading.Thread.Sleep(EffectiveDelayMs);
    }
}
