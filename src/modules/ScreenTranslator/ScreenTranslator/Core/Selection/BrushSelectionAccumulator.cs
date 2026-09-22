// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Core.Selection;

public sealed class BrushSelectionAccumulator
{
    private readonly IReadOnlyList<RecognizedWord> _selectableWords;
    private readonly HashSet<RecognizedWord> _selectedWords = new();

    public BrushSelectionAccumulator(IReadOnlyList<RecognizedWord> selectableWords)
    {
        ArgumentNullException.ThrowIfNull(selectableWords);
        _selectableWords = selectableWords;
    }

    public int Count => _selectedWords.Count;

    public IReadOnlyList<RecognizedWord> SelectedWords => _selectableWords
        .Where(_selectedWords.Contains)
        .ToList();

    public IReadOnlyList<RecognizedWord> AddStroke(IReadOnlyList<PhysicalPoint> strokePoints, double brushRadius)
    {
        ArgumentNullException.ThrowIfNull(strokePoints);

        IReadOnlyList<RecognizedWord> strokeWords = BrushHitTestHelper.FindIntersectedWords(
            _selectableWords,
            strokePoints,
            brushRadius);
        foreach (RecognizedWord word in strokeWords)
        {
            _selectedWords.Add(word);
        }

        return SelectedWords;
    }

    public void Clear()
    {
        _selectedWords.Clear();
    }
}
