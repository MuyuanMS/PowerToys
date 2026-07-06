// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;

namespace AdvancedPaste.Services;

/// <summary>
/// Provides functionality for sending text to the active application by simulating keystrokes.
/// </summary>
public interface IKeystrokeService
{
    /// <summary>
    /// Sends the specified text to the active application as a sequence of keystrokes.
    /// </summary>
    /// <param name="text">The text to send as simulated keystrokes.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown when the operation is cancelled via <paramref name="cancellationToken"/>.
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when the underlying SendInput call fails to send all input events.
    /// </exception>
    void SendTextAsKeystrokes(string text, CancellationToken cancellationToken = default);
}
