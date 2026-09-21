// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Core.Selection;

public static class BrushHitTestHelper
{
    public static IReadOnlyList<RecognizedWord> FindIntersectedWords(
        IReadOnlyList<RecognizedWord> words,
        IReadOnlyList<PhysicalPoint> strokePoints,
        double brushRadius)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(strokePoints);

        if (strokePoints.Count == 0)
        {
            return Array.Empty<RecognizedWord>();
        }

        List<RecognizedWord> selectedWords = new();
        foreach (RecognizedWord word in words)
        {
            PhysicalRect hitBounds = Inflate(word.BoundingBox, Math.Max(0, brushRadius));
            if (StrokeIntersectsRect(strokePoints, hitBounds))
            {
                selectedWords.Add(word);
            }
        }

        return selectedWords;
    }

    private static PhysicalRect Inflate(PhysicalRect rect, double amount)
    {
        return new PhysicalRect(
            rect.X - amount,
            rect.Y - amount,
            rect.Width + (amount * 2),
            rect.Height + (amount * 2));
    }

    private static bool StrokeIntersectsRect(IReadOnlyList<PhysicalPoint> points, PhysicalRect rect)
    {
        if (rect.Contains(points[0]))
        {
            return true;
        }

        for (int i = 1; i < points.Count; i++)
        {
            if (rect.Contains(points[i]) || SegmentIntersectsRect(points[i - 1], points[i], rect))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentIntersectsRect(PhysicalPoint start, PhysicalPoint end, PhysicalRect rect)
    {
        PhysicalPoint topLeft = new(rect.Left, rect.Top);
        PhysicalPoint topRight = new(rect.Right, rect.Top);
        PhysicalPoint bottomRight = new(rect.Right, rect.Bottom);
        PhysicalPoint bottomLeft = new(rect.Left, rect.Bottom);

        return SegmentsIntersect(start, end, topLeft, topRight) ||
               SegmentsIntersect(start, end, topRight, bottomRight) ||
               SegmentsIntersect(start, end, bottomRight, bottomLeft) ||
               SegmentsIntersect(start, end, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(PhysicalPoint firstStart, PhysicalPoint firstEnd, PhysicalPoint secondStart, PhysicalPoint secondEnd)
    {
        double firstCrossStart = Cross(firstStart, firstEnd, secondStart);
        double firstCrossEnd = Cross(firstStart, firstEnd, secondEnd);
        double secondCrossStart = Cross(secondStart, secondEnd, firstStart);
        double secondCrossEnd = Cross(secondStart, secondEnd, firstEnd);

        if (((firstCrossStart > 0 && firstCrossEnd < 0) || (firstCrossStart < 0 && firstCrossEnd > 0)) &&
            ((secondCrossStart > 0 && secondCrossEnd < 0) || (secondCrossStart < 0 && secondCrossEnd > 0)))
        {
            return true;
        }

        const double tolerance = 0.0001;
        return (Math.Abs(firstCrossStart) <= tolerance && IsOnSegment(firstStart, firstEnd, secondStart)) ||
               (Math.Abs(firstCrossEnd) <= tolerance && IsOnSegment(firstStart, firstEnd, secondEnd)) ||
               (Math.Abs(secondCrossStart) <= tolerance && IsOnSegment(secondStart, secondEnd, firstStart)) ||
               (Math.Abs(secondCrossEnd) <= tolerance && IsOnSegment(secondStart, secondEnd, firstEnd));
    }

    private static double Cross(PhysicalPoint start, PhysicalPoint end, PhysicalPoint point)
    {
        return ((end.X - start.X) * (point.Y - start.Y)) -
               ((end.Y - start.Y) * (point.X - start.X));
    }

    private static bool IsOnSegment(PhysicalPoint start, PhysicalPoint end, PhysicalPoint point)
    {
        return point.X >= Math.Min(start.X, end.X) &&
               point.X <= Math.Max(start.X, end.X) &&
               point.Y >= Math.Min(start.Y, end.Y) &&
               point.Y <= Math.Max(start.Y, end.Y);
    }
}
