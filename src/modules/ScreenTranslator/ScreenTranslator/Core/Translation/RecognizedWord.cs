// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace ScreenTranslator.Core.Translation;

public sealed record RecognizedWord(
    string Text,
    PhysicalRect BoundingBox,
    int LineIndex,
    int WordIndex,
    double Confidence = 1.0,
    IReadOnlyList<PhysicalPoint>? PolygonVertices = null);
