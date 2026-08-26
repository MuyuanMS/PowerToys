// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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
    /// <param name="cancellationToken">Token used to stop an in-progress keystroke paste.</param>
    /// <returns><see langword="true"/> when all text was sent; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    bool SendTextAsKeystrokes(string text, CancellationToken cancellationToken = default);
}
