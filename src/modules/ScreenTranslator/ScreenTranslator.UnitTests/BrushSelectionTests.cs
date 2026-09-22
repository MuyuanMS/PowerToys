// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenTranslator.Core.Selection;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.UnitTests;

[TestClass]
public class BrushSelectionTests
{
    [TestMethod]
    public void FindIntersectedWords_SelectsWordCrossedBetweenSparsePointerSamples()
    {
        RecognizedWord crossedWord = new("selected", new PhysicalRect(40, 40, 30, 20), 0, 0);
        RecognizedWord distantWord = new("ignored", new PhysicalRect(40, 90, 30, 20), 1, 0);
        IReadOnlyList<PhysicalPoint> stroke = new[]
        {
            new PhysicalPoint(10, 50),
            new PhysicalPoint(100, 50),
        };

        IReadOnlyList<RecognizedWord> result = BrushHitTestHelper.FindIntersectedWords(
            new[] { crossedWord, distantWord },
            stroke,
            brushRadius: 4);

        CollectionAssert.AreEqual(new[] { crossedWord }, new List<RecognizedWord>(result));
    }

    [TestMethod]
    public void FindIntersectedWords_UsesBrushRadiusWithoutSelectingDistantWord()
    {
        RecognizedWord nearWord = new("near", new PhysicalRect(40, 40, 30, 20), 0, 0);
        RecognizedWord distantWord = new("far", new PhysicalRect(40, 80, 30, 20), 1, 0);
        IReadOnlyList<PhysicalPoint> stroke = new[]
        {
            new PhysicalPoint(10, 66),
            new PhysicalPoint(100, 66),
        };

        IReadOnlyList<RecognizedWord> result = BrushHitTestHelper.FindIntersectedWords(
            new[] { nearWord, distantWord },
            stroke,
            brushRadius: 7);

        CollectionAssert.AreEqual(new[] { nearWord }, new List<RecognizedWord>(result));
    }

    [TestMethod]
    public void BuildPhrase_PreservesReadingOrderAcrossLineBoundary()
    {
        RecognizedWord lineOneEnd = new("brown", new PhysicalRect(80, 10, 45, 20), 0, 2);
        RecognizedWord lineTwoStart = new("fox", new PhysicalRect(10, 40, 30, 20), 1, 0);

        BrushPhrase? phrase = BrushPhraseBuilder.Build(new[] { lineTwoStart, lineOneEnd });

        Assert.IsNotNull(phrase);
        Assert.AreEqual("brown fox", phrase.Text);
        Assert.AreEqual(new PhysicalRect(10, 10, 115, 50), phrase.BoundingBox);
        CollectionAssert.AreEqual(
            new[] { lineOneEnd, lineTwoStart },
            new List<RecognizedWord>(phrase.Words));
    }

    [TestMethod]
    public void AddStroke_AccumulatesSelectionAcrossDisconnectedStrokes()
    {
        RecognizedWord firstLine = new("first", new PhysicalRect(10, 10, 30, 20), 0, 0);
        RecognizedWord secondLine = new("second", new PhysicalRect(10, 60, 45, 20), 1, 0);
        BrushSelectionAccumulator accumulator = new(new[] { firstLine, secondLine });

        accumulator.AddStroke(
            new[]
            {
                new PhysicalPoint(0, 20),
                new PhysicalPoint(50, 20),
            },
            brushRadius: 4);
        IReadOnlyList<RecognizedWord> result = accumulator.AddStroke(
            new[]
            {
                new PhysicalPoint(0, 70),
                new PhysicalPoint(60, 70),
            },
            brushRadius: 4);

        CollectionAssert.AreEqual(new[] { firstLine, secondLine }, new List<RecognizedWord>(result));
    }

    [TestMethod]
    public void AddStroke_DoesNotBridgeBetweenDisconnectedStrokes()
    {
        RecognizedWord firstLineEnd = new("first", new PhysicalRect(120, 10, 40, 20), 0, 0);
        RecognizedWord diagonalBridge = new("bridge", new PhysicalRect(70, 35, 40, 20), 0, 1);
        RecognizedWord secondLineStart = new("second", new PhysicalRect(10, 70, 50, 20), 1, 0);
        BrushSelectionAccumulator accumulator = new(new[] { firstLineEnd, diagonalBridge, secondLineStart });

        accumulator.AddStroke(
            new[]
            {
                new PhysicalPoint(110, 20),
                new PhysicalPoint(170, 20),
            },
            brushRadius: 4);
        IReadOnlyList<RecognizedWord> result = accumulator.AddStroke(
            new[]
            {
                new PhysicalPoint(0, 80),
                new PhysicalPoint(70, 80),
            },
            brushRadius: 4);

        CollectionAssert.AreEqual(new[] { firstLineEnd, secondLineStart }, new List<RecognizedWord>(result));
    }

    [TestMethod]
    public void Clear_RemovesAccumulatedSelection()
    {
        RecognizedWord word = new("selected", new PhysicalRect(10, 10, 30, 20), 0, 0);
        BrushSelectionAccumulator accumulator = new(new[] { word });
        accumulator.AddStroke(
            new[]
            {
                new PhysicalPoint(0, 20),
                new PhysicalPoint(50, 20),
            },
            brushRadius: 4);

        accumulator.Clear();

        Assert.AreEqual(0, accumulator.Count);
        CollectionAssert.AreEqual(System.Array.Empty<RecognizedWord>(), new List<RecognizedWord>(accumulator.SelectedWords));
    }

    [TestMethod]
    public void BuildPhrase_UsesReadingOrderIndependentOfStrokeOrder()
    {
        RecognizedWord lineOneStart = new("alpha", new PhysicalRect(10, 10, 40, 20), 0, 0);
        RecognizedWord lineTwoStart = new("omega", new PhysicalRect(10, 60, 50, 20), 1, 0);
        BrushSelectionAccumulator accumulator = new(new[] { lineOneStart, lineTwoStart });
        accumulator.AddStroke(
            new[]
            {
                new PhysicalPoint(0, 70),
                new PhysicalPoint(70, 70),
            },
            brushRadius: 4);
        accumulator.AddStroke(
            new[]
            {
                new PhysicalPoint(0, 20),
                new PhysicalPoint(60, 20),
            },
            brushRadius: 4);

        BrushPhrase? phrase = BrushPhraseBuilder.Build(accumulator.SelectedWords);

        Assert.IsNotNull(phrase);
        Assert.AreEqual("alpha omega", phrase.Text);
        CollectionAssert.AreEqual(new[] { lineOneStart, lineTwoStart }, new List<RecognizedWord>(phrase.Words));
    }

    [TestMethod]
    public void BuildPhrase_UsesNaturalPunctuationSpacing()
    {
        RecognizedWord hello = new("Hello", new PhysicalRect(0, 0, 30, 10), 0, 0);
        RecognizedWord comma = new(",", new PhysicalRect(31, 0, 5, 10), 0, 1);
        RecognizedWord world = new("world", new PhysicalRect(40, 0, 30, 10), 0, 2);

        BrushPhrase? phrase = BrushPhraseBuilder.Build(new[] { world, comma, hello });

        Assert.IsNotNull(phrase);
        Assert.AreEqual("Hello, world", phrase.Text);
    }
}
