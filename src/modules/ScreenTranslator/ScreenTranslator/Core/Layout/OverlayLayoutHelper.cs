// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Core.Layout;

/// <summary>
/// Pure layout and coordinate helper keeping physical pixels canonical across multiple monitors and DPI scales.
/// </summary>
public static class OverlayLayoutHelper
{
    private const double MaxInitialCardWidthFraction = 0.9;
    private const double MaxInitialSingleLineCardHeightFraction = 0.35;
    private const double MaxInitialMultilineCardHeightFraction = 0.6;

    /// <summary>
    /// Converts a canonical physical rectangle into DIP (device independent pixel) coordinates relative to a specific screen's top-left.
    /// </summary>
    public static (double LeftDip, double TopDip, double WidthDip, double HeightDip) PhysicalToDip(
        PhysicalRect physicalRect,
        PhysicalRect screenBoundsPhysical,
        double dpiScaleX,
        double dpiScaleY)
    {
        if (dpiScaleX <= 0)
        {
            dpiScaleX = 1.0;
        }

        if (dpiScaleY <= 0)
        {
            dpiScaleY = 1.0;
        }

        double relativePhysicalX = physicalRect.X - screenBoundsPhysical.X;
        double relativePhysicalY = physicalRect.Y - screenBoundsPhysical.Y;

        double leftDip = relativePhysicalX / dpiScaleX;
        double topDip = relativePhysicalY / dpiScaleY;
        double widthDip = physicalRect.Width / dpiScaleX;
        double heightDip = physicalRect.Height / dpiScaleY;

        return (leftDip, topDip, widthDip, heightDip);
    }

    /// <summary>
    /// Converts local DIP coordinates on a specific screen into canonical physical coordinates on the virtual desktop.
    /// </summary>
    public static PhysicalRect DipToPhysical(
        double leftDip,
        double topDip,
        double widthDip,
        double heightDip,
        PhysicalRect screenBoundsPhysical,
        double dpiScaleX,
        double dpiScaleY)
    {
        if (dpiScaleX <= 0)
        {
            dpiScaleX = 1.0;
        }

        if (dpiScaleY <= 0)
        {
            dpiScaleY = 1.0;
        }

        double physicalX = screenBoundsPhysical.X + (leftDip * dpiScaleX);
        double physicalY = screenBoundsPhysical.Y + (topDip * dpiScaleY);
        double physicalWidth = widthDip * dpiScaleX;
        double physicalHeight = heightDip * dpiScaleY;

        return new PhysicalRect(physicalX, physicalY, physicalWidth, physicalHeight);
    }

    /// <summary>
    /// Estimates font size based on bounding box height in DIP (proportional to line height, inspired by AI Dev Gallery).
    /// </summary>
    public static double CalculateEstimatedFontSize(
        double heightDip,
        double scaleFactor = 0.85,
        double minFontSize = 9.0,
        double maxFontSize = 48.0)
    {
        if (heightDip <= 0)
        {
            return 12.0;
        }

        double calculated = heightDip * scaleFactor;
        return Math.Max(minFontSize, Math.Min(maxFontSize, calculated));
    }

    /// <summary>
    /// Positions a detail menu away from its primary menu and selected content, preferring the outward side.
    /// </summary>
    public static double CalculateDetailMenuTop(
        double overlayHeight,
        double detailHeight,
        double primaryTop,
        double primaryHeight,
        double cardTop,
        double cardHeight,
        bool primaryIsAboveCard,
        double margin = 8.0,
        double gap = 4.0)
    {
        double outwardTop = primaryIsAboveCard
            ? primaryTop - detailHeight - gap
            : primaryTop + primaryHeight + gap;
        double oppositeCardTop = primaryIsAboveCard
            ? cardTop + cardHeight + gap
            : cardTop - detailHeight - gap;
        double preferredTop = outwardTop >= margin && outwardTop + detailHeight <= overlayHeight - margin
            ? outwardTop
            : oppositeCardTop;

        return Math.Clamp(preferredTop, margin, Math.Max(margin, overlayHeight - detailHeight - margin));
    }

    /// <summary>
    /// Combines multiple word bounding rects into a single bounding rect for a line.
    /// </summary>
    public static PhysicalRect CombineWordRects(IEnumerable<PhysicalRect> wordRects)
    {
        PhysicalRect combined = PhysicalRect.Empty;
        if (wordRects == null)
        {
            return combined;
        }

        foreach (var rect in wordRects)
        {
            if (!rect.IsEmpty)
            {
                combined = combined.Union(rect);
            }
        }

        return combined;
    }

    /// <summary>
    /// Groups adjacent OCR lines that form one wrapped text block using the selected provider-neutral strategy.
    /// </summary>
    public static IReadOnlyList<TranslationLine> GroupAdjacentTextLines(
        IEnumerable<TranslationLine> lines,
        TextBlockGroupingStrategy strategy = TextBlockGroupingStrategy.Legacy)
    {
        return strategy switch
        {
            TextBlockGroupingStrategy.Legacy => GroupAdjacentTextLinesLegacy(lines),
            _ => ScoredTextBlockGrouper.Group(lines),
        };
    }

    internal static IReadOnlyList<TranslationLine> GroupAdjacentTextLinesLegacy(IEnumerable<TranslationLine> lines)
    {
        if (lines == null)
        {
            return Array.Empty<TranslationLine>();
        }

        List<TranslationLine> ordered = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text) && !line.BoundingBox.IsEmpty)
            .OrderBy(line => line.BoundingBox.Top)
            .ThenBy(line => line.BoundingBox.Left)
            .ToList();

        if (ordered.Count < 2)
        {
            return ordered;
        }

        ordered = MergeSameBaselineFragments(ordered);

        List<List<TranslationLine>> groups = new();
        foreach (TranslationLine line in ordered)
        {
            List<TranslationLine>? bestGroup = null;
            double bestVerticalGap = double.MaxValue;

            foreach (List<TranslationLine> group in groups)
            {
                TranslationLine previous = group[^1];
                if (CanGroupLines(previous, line, out double verticalGap) && verticalGap < bestVerticalGap)
                {
                    bestGroup = group;
                    bestVerticalGap = verticalGap;
                }
            }

            if (bestGroup == null)
            {
                groups.Add(new List<TranslationLine> { line });
            }
            else
            {
                bestGroup.Add(line);
            }
        }

        return groups.Select(MergeTextLineGroup).ToList();
    }

    internal static List<TranslationLine> MergeSameBaselineFragments(IReadOnlyList<TranslationLine> lines)
    {
        List<List<TranslationLine>> rows = new();
        foreach (TranslationLine line in lines)
        {
            List<TranslationLine>? bestRow = null;
            double bestCenterDistance = double.MaxValue;

            foreach (List<TranslationLine> row in rows)
            {
                TranslationLine reference = row[0];
                if (!SharesBaseline(reference.BoundingBox, line.BoundingBox, out double centerDistance))
                {
                    continue;
                }

                if (centerDistance < bestCenterDistance)
                {
                    bestRow = row;
                    bestCenterDistance = centerDistance;
                }
            }

            if (bestRow == null)
            {
                rows.Add(new List<TranslationLine> { line });
            }
            else
            {
                bestRow.Add(line);
            }
        }

        List<TranslationLine> mergedLines = new();
        foreach (List<TranslationLine> row in rows)
        {
            List<TranslationLine> orderedRow = row.OrderBy(line => line.BoundingBox.Left).ToList();
            List<TranslationLine> fragmentGroup = new() { orderedRow[0] };

            for (int index = 1; index < orderedRow.Count; index++)
            {
                TranslationLine previous = fragmentGroup[^1];
                TranslationLine current = orderedRow[index];
                double referenceHeight = Math.Max(1.0, (previous.BoundingBox.Height + current.BoundingBox.Height) / 2.0);
                double horizontalGap = current.BoundingBox.Left - previous.BoundingBox.Right;
                double maximumFragmentGap = Math.Max(40.0, referenceHeight * 4.0);

                if (horizontalGap <= maximumFragmentGap)
                {
                    fragmentGroup.Add(current);
                }
                else
                {
                    mergedLines.Add(MergeSameBaselineGroup(fragmentGroup));
                    fragmentGroup = new List<TranslationLine> { current };
                }
            }

            mergedLines.Add(MergeSameBaselineGroup(fragmentGroup));
        }

        return mergedLines
            .OrderBy(line => line.BoundingBox.Top)
            .ThenBy(line => line.BoundingBox.Left)
            .ToList();
    }

    private static bool SharesBaseline(PhysicalRect first, PhysicalRect second, out double centerDistance)
    {
        double firstCenter = first.Top + (first.Height / 2.0);
        double secondCenter = second.Top + (second.Height / 2.0);
        centerDistance = Math.Abs(firstCenter - secondCenter);

        double overlapTop = Math.Max(first.Top, second.Top);
        double overlapBottom = Math.Min(first.Bottom, second.Bottom);
        double verticalOverlap = Math.Max(0, overlapBottom - overlapTop);
        double overlapRatio = verticalOverlap / Math.Max(1.0, Math.Min(first.Height, second.Height));
        double maximumHeight = Math.Max(1.0, Math.Max(first.Height, second.Height));
        double bottomDistance = Math.Abs(first.Bottom - second.Bottom);

        return overlapRatio >= 0.2 ||
               centerDistance <= (maximumHeight * 0.65) ||
               bottomDistance <= Math.Max(6.0, maximumHeight * 0.5);
    }

    private static TranslationLine MergeSameBaselineGroup(IReadOnlyList<TranslationLine> group)
    {
        if (group.Count == 1)
        {
            return group[0];
        }

        PhysicalRect bounds = group[0].BoundingBox;
        double confidenceTotal = 0;
        string text = group[0].Text.Trim();

        for (int index = 0; index < group.Count; index++)
        {
            TranslationLine line = group[index];
            bounds = bounds.Union(line.BoundingBox);
            confidenceTotal += line.Confidence;

            if (index > 0)
            {
                text = JoinSameBaselineText(text, line.Text.Trim());
            }
        }

        return new TranslationLine(
            text,
            bounds,
            confidenceTotal / group.Count,
            CreateRectanglePolygon(bounds),
            group.Max(line => Math.Max(1, line.SourceLineCount)));
    }

    private static string JoinSameBaselineText(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        const string closingPunctuation = ".,;:!?%)]}";
        const string openingPunctuation = "([{";
        bool omitSpace = closingPunctuation.Contains(right[0]) ||
                         openingPunctuation.Contains(left[^1]);

        return omitSpace ? left + right : left + " " + right;
    }

    private static bool CanGroupLines(TranslationLine previous, TranslationLine current, out double verticalGap)
    {
        double previousLineHeight = previous.BoundingBox.Height / Math.Max(1, previous.SourceLineCount);
        double currentLineHeight = current.BoundingBox.Height / Math.Max(1, current.SourceLineCount);
        double smallerLineHeight = Math.Min(previousLineHeight, currentLineHeight);
        double largerLineHeight = Math.Max(1.0, Math.Max(previousLineHeight, currentLineHeight));
        if ((smallerLineHeight / largerLineHeight) < 0.8)
        {
            verticalGap = double.MaxValue;
            return false;
        }

        double referenceHeight = Math.Max(1.0, (previousLineHeight + currentLineHeight) / 2.0);
        verticalGap = current.BoundingBox.Top - previous.BoundingBox.Bottom;
        if (verticalGap < (-0.35 * referenceHeight) || verticalGap > (0.8 * referenceHeight))
        {
            return false;
        }

        double overlapRight = Math.Min(previous.BoundingBox.Right, current.BoundingBox.Right);
        double overlapLeft = Math.Max(previous.BoundingBox.Left, current.BoundingBox.Left);
        double overlap = Math.Max(0, overlapRight - overlapLeft);
        double overlapRatio = overlap / Math.Max(1.0, Math.Min(previous.BoundingBox.Width, current.BoundingBox.Width));
        double leftAlignmentTolerance = Math.Max(24.0, referenceHeight * 1.5);
        bool leftAligned = Math.Abs(previous.BoundingBox.Left - current.BoundingBox.Left) <= leftAlignmentTolerance;

        return leftAligned || overlapRatio >= 0.6;
    }

    internal static TranslationLine MergeTextLineGroup(IReadOnlyList<TranslationLine> group)
    {
        if (group.Count == 1)
        {
            return group[0];
        }

        PhysicalRect bounds = group[0].BoundingBox;
        double confidenceTotal = 0;
        int sourceLineCount = 0;
        List<string> textParts = new();

        foreach (TranslationLine line in group)
        {
            bounds = bounds.Union(line.BoundingBox);
            confidenceTotal += line.Confidence;
            sourceLineCount += Math.Max(1, line.SourceLineCount);

            string text = line.Text.Trim();
            if (textParts.Count > 0 && textParts[^1].EndsWith('-'))
            {
                textParts[^1] = textParts[^1][..^1] + text;
            }
            else
            {
                textParts.Add(text);
            }
        }

        return new TranslationLine(
            string.Join(" ", textParts),
            bounds,
            confidenceTotal / group.Count,
            CreateRectanglePolygon(bounds),
            sourceLineCount);
    }

    private static IReadOnlyList<PhysicalPoint> CreateRectanglePolygon(PhysicalRect bounds)
    {
        return new[]
        {
            new PhysicalPoint(bounds.Left, bounds.Top),
            new PhysicalPoint(bounds.Right, bounds.Top),
            new PhysicalPoint(bounds.Right, bounds.Bottom),
            new PhysicalPoint(bounds.Left, bounds.Bottom),
        };
    }

    /// <summary>
    /// Clamps a physical rectangle within screen bounds.
    /// </summary>
    public static PhysicalRect ClampToScreen(PhysicalRect rect, PhysicalRect screenBounds)
    {
        if (rect.IsEmpty || screenBounds.IsEmpty)
        {
            return rect;
        }

        double left = Math.Max(screenBounds.Left, Math.Min(rect.Left, screenBounds.Right));
        double top = Math.Max(screenBounds.Top, Math.Min(rect.Top, screenBounds.Bottom));
        double right = Math.Max(left, Math.Min(rect.Right, screenBounds.Right));
        double bottom = Math.Max(top, Math.Min(rect.Bottom, screenBounds.Bottom));

        return new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static (double Width, double MinHeight) CalculateInitialCardSize(
        double widthDip,
        double heightDip,
        double captureWidthDip,
        double captureHeightDip,
        int sourceLineCount)
    {
        double safeCaptureWidth = IsFinitePositive(captureWidthDip) ? captureWidthDip : Math.Max(24.0, widthDip);
        double safeCaptureHeight = IsFinitePositive(captureHeightDip) ? captureHeightDip : Math.Max(18.0, heightDip);
        double maximumWidth = Math.Max(24.0, safeCaptureWidth * MaxInitialCardWidthFraction);
        double maximumHeight = Math.Max(
            18.0,
            safeCaptureHeight * (sourceLineCount > 1 ? MaxInitialMultilineCardHeightFraction : MaxInitialSingleLineCardHeightFraction));

        return (
            Math.Min(Math.Max(24.0, IsFinitePositive(widthDip) ? widthDip : 24.0), maximumWidth),
            Math.Min(Math.Max(18.0, IsFinitePositive(heightDip) ? heightDip : 18.0), maximumHeight));
    }

    public static IReadOnlyList<TranslationLine> SanitizeOcrLineGeometry(
        IEnumerable<TranslationLine> lines,
        PhysicalRect capturedRegion)
    {
        if (lines == null)
        {
            return Array.Empty<TranslationLine>();
        }

        List<TranslationLine> sanitized = new();
        foreach (TranslationLine line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            PhysicalRect bounds = SanitizeTextBounds(line.BoundingBox, capturedRegion, line.SourceLineCount);
            if (bounds.IsEmpty)
            {
                continue;
            }

            IReadOnlyList<RecognizedWord>? words = SanitizeWords(line.Words, capturedRegion);
            sanitized.Add(line with
            {
                BoundingBox = bounds,
                PolygonVertices = SanitizePolygon(line.PolygonVertices, bounds, capturedRegion),
                Words = words,
            });
        }

        return sanitized;
    }

    public static IReadOnlyList<TranslatedLine> SanitizeTranslatedLineGeometry(
        IEnumerable<TranslatedLine> lines,
        PhysicalRect capturedRegion)
    {
        if (lines == null)
        {
            return Array.Empty<TranslatedLine>();
        }

        List<TranslatedLine> sanitized = new();
        foreach (TranslatedLine line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.TranslatedText) && string.IsNullOrWhiteSpace(line.OriginalText))
            {
                continue;
            }

            PhysicalRect bounds = SanitizeTextBounds(line.BoundingBox, capturedRegion, line.SourceLineCount);
            if (bounds.IsEmpty)
            {
                continue;
            }

            sanitized.Add(line with
            {
                BoundingBox = bounds,
                PolygonVertices = SanitizePolygon(line.PolygonVertices, bounds, capturedRegion),
            });
        }

        return sanitized;
    }

    private static IReadOnlyList<RecognizedWord>? SanitizeWords(
        IReadOnlyList<RecognizedWord>? words,
        PhysicalRect capturedRegion)
    {
        if (words == null || words.Count == 0)
        {
            return words;
        }

        List<RecognizedWord> sanitized = new(words.Count);
        foreach (RecognizedWord word in words)
        {
            PhysicalRect bounds = ClampFiniteIntersection(word.BoundingBox, capturedRegion);
            if (bounds.IsEmpty)
            {
                continue;
            }

            sanitized.Add(word with
            {
                BoundingBox = bounds,
                PolygonVertices = SanitizePolygon(word.PolygonVertices, bounds, capturedRegion),
            });
        }

        return sanitized;
    }

    private static PhysicalRect SanitizeTextBounds(
        PhysicalRect bounds,
        PhysicalRect capturedRegion,
        int sourceLineCount)
    {
        if (!IsValidRegion(capturedRegion))
        {
            return bounds;
        }

        PhysicalRect clamped = ClampFiniteIntersection(bounds, capturedRegion);
        if (clamped.IsEmpty)
        {
            return CreateFallbackTextBounds(bounds, capturedRegion);
        }

        double area = clamped.Width * clamped.Height;
        double captureArea = capturedRegion.Width * capturedRegion.Height;
        bool implausiblyLarge = captureArea > 0 &&
                                (area > captureArea * 0.75 ||
                                 (clamped.Width > capturedRegion.Width * 0.95 &&
                                  clamped.Height > capturedRegion.Height * 0.65));
        if (!implausiblyLarge)
        {
            return clamped;
        }

        double maximumHeightFraction = sourceLineCount > 1
            ? MaxInitialMultilineCardHeightFraction
            : MaxInitialSingleLineCardHeightFraction;
        double width = Math.Min(clamped.Width, Math.Max(24.0, capturedRegion.Width * MaxInitialCardWidthFraction));
        double height = Math.Min(clamped.Height, Math.Max(18.0, capturedRegion.Height * maximumHeightFraction));
        double left = Math.Clamp(clamped.Left, capturedRegion.Left, Math.Max(capturedRegion.Left, capturedRegion.Right - width));
        double top = Math.Clamp(clamped.Top, capturedRegion.Top, Math.Max(capturedRegion.Top, capturedRegion.Bottom - height));
        return new PhysicalRect(left, top, width, height);
    }

    private static PhysicalRect ClampFiniteIntersection(PhysicalRect bounds, PhysicalRect capturedRegion)
    {
        if (!IsValidRegion(bounds) || !IsValidRegion(capturedRegion))
        {
            return PhysicalRect.Empty;
        }

        double left = Math.Max(bounds.Left, capturedRegion.Left);
        double top = Math.Max(bounds.Top, capturedRegion.Top);
        double right = Math.Min(bounds.Right, capturedRegion.Right);
        double bottom = Math.Min(bounds.Bottom, capturedRegion.Bottom);
        if (right <= left || bottom <= top)
        {
            return PhysicalRect.Empty;
        }

        return new PhysicalRect(left, top, right - left, bottom - top);
    }

    private static PhysicalRect CreateFallbackTextBounds(PhysicalRect originalBounds, PhysicalRect capturedRegion)
    {
        if (!IsValidRegion(capturedRegion))
        {
            return PhysicalRect.Empty;
        }

        double width = Math.Min(capturedRegion.Width, Math.Max(24.0, capturedRegion.Width * 0.35));
        double height = Math.Min(capturedRegion.Height, Math.Max(18.0, capturedRegion.Height * 0.06));
        double requestedLeft = double.IsFinite(originalBounds.Left) ? originalBounds.Left : capturedRegion.Left;
        double requestedTop = double.IsFinite(originalBounds.Top) ? originalBounds.Top : capturedRegion.Top;
        double left = Math.Clamp(requestedLeft, capturedRegion.Left, Math.Max(capturedRegion.Left, capturedRegion.Right - width));
        double top = Math.Clamp(requestedTop, capturedRegion.Top, Math.Max(capturedRegion.Top, capturedRegion.Bottom - height));
        return new PhysicalRect(left, top, width, height);
    }

    private static IReadOnlyList<PhysicalPoint>? SanitizePolygon(
        IReadOnlyList<PhysicalPoint>? polygon,
        PhysicalRect bounds,
        PhysicalRect capturedRegion)
    {
        if (polygon == null || polygon.Count == 0)
        {
            return CreateRectanglePolygon(bounds);
        }

        List<PhysicalPoint> points = new(polygon.Count);
        foreach (PhysicalPoint point in polygon)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                continue;
            }

            points.Add(new PhysicalPoint(
                Math.Clamp(point.X, capturedRegion.Left, capturedRegion.Right),
                Math.Clamp(point.Y, capturedRegion.Top, capturedRegion.Bottom)));
        }

        return points.Count > 0 ? points : CreateRectanglePolygon(bounds);
    }

    private static bool IsValidRegion(PhysicalRect rect)
    {
        return !rect.IsEmpty &&
               double.IsFinite(rect.X) &&
               double.IsFinite(rect.Y) &&
               double.IsFinite(rect.Width) &&
               double.IsFinite(rect.Height);
    }

    private static bool IsFinitePositive(double value)
    {
        return double.IsFinite(value) && value > 0;
    }

    /// <summary>
    /// Finds which screen in physical coordinates contains the given point or rectangle.
    /// </summary>
    public static PhysicalRect? FindContainingScreen(PhysicalPoint point, IEnumerable<PhysicalRect> screens)
    {
        if (screens == null)
        {
            return null;
        }

        foreach (var screen in screens)
        {
            if (screen.Contains(point))
            {
                return screen;
            }
        }

        return null;
    }

    public static PhysicalRect? FindContainingScreen(PhysicalRect rect, IEnumerable<PhysicalRect> screens)
    {
        if (screens == null)
        {
            return null;
        }

        PhysicalPoint center = new(rect.X + (rect.Width / 2.0), rect.Y + (rect.Height / 2.0));
        var found = FindContainingScreen(center, screens);
        if (found.HasValue)
        {
            return found;
        }

        return screens.FirstOrDefault();
    }
}
