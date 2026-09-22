// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace ScreenTranslator.Keyboard;

public readonly record struct KeyboardShortcutContext(
    bool HasVisibleResultOverlay,
    bool IsResultOverlayActive,
    bool IsEditingResultCard,
    bool HasSelectedResultCard,
    bool CanUndoSelectedResultCard);
