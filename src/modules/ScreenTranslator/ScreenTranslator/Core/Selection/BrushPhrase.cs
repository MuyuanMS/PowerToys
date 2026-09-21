// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Core.Selection;

public sealed record BrushPhrase(string Text, PhysicalRect BoundingBox, IReadOnlyList<RecognizedWord> Words);
