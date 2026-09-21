// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenTranslator.Core.Layout;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.UnitTests;

[TestClass]
public class OverlayLayoutHelperTests
{
    [TestMethod]
    public void PhysicalToDip_TransformsCorrectly_WithScale1()
    {
        PhysicalRect physical = new(100, 200, 300, 400);
        PhysicalRect screenBounds = new(0, 0, 1920, 1080);

        var (leftDip, topDip, widthDip, heightDip) = OverlayLayoutHelper.PhysicalToDip(
            physical, screenBounds, 1.0, 1.0);

        Assert.AreEqual(100.0, leftDip, 0.001);
        Assert.AreEqual(200.0, topDip, 0.001);
        Assert.AreEqual(300.0, widthDip, 0.001);
        Assert.AreEqual(400.0, heightDip, 0.001);
    }

    [TestMethod]
    public void PhysicalToDip_TransformsCorrectly_WithHighDpiScale()
    {
        PhysicalRect physical = new(300, 600, 450, 300);
        PhysicalRect screenBounds = new(0, 0, 3840, 2160);

        var (leftDip, topDip, widthDip, heightDip) = OverlayLayoutHelper.PhysicalToDip(
            physical, screenBounds, 1.5, 1.5);

        Assert.AreEqual(200.0, leftDip, 0.001);
        Assert.AreEqual(400.0, topDip, 0.001);
        Assert.AreEqual(300.0, widthDip, 0.001);
        Assert.AreEqual(200.0, heightDip, 0.001);
    }

    [TestMethod]
    public void MultiMonitor_PhysicalToDip_OffsetsRelativeToScreenOrigin()
    {
        // Second monitor placed to the right of primary monitor
        PhysicalRect screenBounds = new(1920, 0, 1920, 1080);
        PhysicalRect physicalRect = new(2020, 100, 200, 50);

        var (leftDip, topDip, widthDip, heightDip) = OverlayLayoutHelper.PhysicalToDip(
            physicalRect, screenBounds, 1.0, 1.0);

        Assert.AreEqual(100.0, leftDip, 0.001);
        Assert.AreEqual(100.0, topDip, 0.001);
        Assert.AreEqual(200.0, widthDip, 0.001);
        Assert.AreEqual(50.0, heightDip, 0.001);
    }

    [TestMethod]
    public void DipToPhysical_RoundTripsAccurately()
    {
        PhysicalRect screenBounds = new(1920, 0, 3840, 2160);
        double dpiX = 2.0;
        double dpiY = 2.0;

        double leftDip = 50.0;
        double topDip = 100.0;
        double widthDip = 400.0;
        double heightDip = 200.0;

        PhysicalRect physical = OverlayLayoutHelper.DipToPhysical(
            leftDip, topDip, widthDip, heightDip, screenBounds, dpiX, dpiY);

        Assert.AreEqual(2020.0, physical.X, 0.001);
        Assert.AreEqual(200.0, physical.Y, 0.001);
        Assert.AreEqual(800.0, physical.Width, 0.001);
        Assert.AreEqual(400.0, physical.Height, 0.001);

        var (roundtripLeftDip, roundtripTopDip, roundtripWidthDip, roundtripHeightDip) =
            OverlayLayoutHelper.PhysicalToDip(physical, screenBounds, dpiX, dpiY);

        Assert.AreEqual(leftDip, roundtripLeftDip, 0.001);
        Assert.AreEqual(topDip, roundtripTopDip, 0.001);
        Assert.AreEqual(widthDip, roundtripWidthDip, 0.001);
        Assert.AreEqual(heightDip, roundtripHeightDip, 0.001);
    }

    [TestMethod]
    public void CalculateEstimatedFontSize_ScalesWithHeightAndClamps()
    {
        // Standard line height: 20 DIP * 0.85 = 17.0
        double size1 = OverlayLayoutHelper.CalculateEstimatedFontSize(20.0);
        Assert.AreEqual(17.0, size1, 0.001);

        // Tiny line height clamped to minimum
        double sizeMin = OverlayLayoutHelper.CalculateEstimatedFontSize(5.0);
        Assert.AreEqual(9.0, sizeMin, 0.001);

        // Very large line height clamped to maximum
        double sizeMax = OverlayLayoutHelper.CalculateEstimatedFontSize(100.0);
        Assert.AreEqual(48.0, sizeMax, 0.001);

        // Zero / negative height returns default 12.0
        double sizeZero = OverlayLayoutHelper.CalculateEstimatedFontSize(0.0);
        Assert.AreEqual(12.0, sizeZero, 0.001);
    }

    [TestMethod]
    public void CalculateDetailMenuTop_PlacesDetailOutwardWithoutMovingPrimaryMenu()
    {
        double detailTop = OverlayLayoutHelper.CalculateDetailMenuTop(
            overlayHeight: 1080,
            detailHeight: 64,
            primaryTop: 300,
            primaryHeight: 48,
            cardTop: 352,
            cardHeight: 40,
            primaryIsAboveCard: true);

        Assert.AreEqual(232.0, detailTop, 0.001);
    }

    [TestMethod]
    public void CalculateDetailMenuTop_UsesOppositeSideOfCardWhenOutwardSideDoesNotFit()
    {
        double detailTop = OverlayLayoutHelper.CalculateDetailMenuTop(
            overlayHeight: 500,
            detailHeight: 80,
            primaryTop: 8,
            primaryHeight: 48,
            cardTop: 60,
            cardHeight: 40,
            primaryIsAboveCard: true);

        Assert.AreEqual(104.0, detailTop, 0.001);
    }

    [TestMethod]
    public void GetContrastingTextColorArgb_UsesDarkTextOnLightBackground()
    {
        Assert.AreEqual(0xFF000000u, OverlayAppearanceHelper.GetContrastingTextColorArgb(0xFFFFFFFFu));
        Assert.AreEqual(0xFFFFFFFFu, OverlayAppearanceHelper.GetContrastingTextColorArgb(0xFF202020u));
    }

    [TestMethod]
    public void GetContrastingTextColorArgb_UsesStableArgbValues()
    {
        Assert.AreEqual(0xFF000000u, OverlayAppearanceHelper.GetContrastingTextColorArgb(0xFFF0F0F0u));
        Assert.AreEqual(0xFFFFFFFFu, OverlayAppearanceHelper.GetContrastingTextColorArgb(0xFF101010u));
    }

    [TestMethod]
    public void CombineWordRects_EmptyOrNull_ReturnsEmpty()
    {
        Assert.IsTrue(OverlayLayoutHelper.CombineWordRects(null!).IsEmpty);
        Assert.IsTrue(OverlayLayoutHelper.CombineWordRects(new List<PhysicalRect>()).IsEmpty);
    }

    [TestMethod]
    public void CombineWordRects_MultipleWords_ReturnsEnclosingUnion()
    {
        var words = new List<PhysicalRect>
        {
            new(10, 20, 30, 15),
            new(45, 20, 40, 15),
            new(90, 20, 50, 15),
        };

        var combined = OverlayLayoutHelper.CombineWordRects(words);

        Assert.AreEqual(10.0, combined.Left, 0.001);
        Assert.AreEqual(20.0, combined.Top, 0.001);
        Assert.AreEqual(140.0, combined.Right, 0.001);
        Assert.AreEqual(35.0, combined.Bottom, 0.001);
        Assert.AreEqual(130.0, combined.Width, 0.001);
        Assert.AreEqual(15.0, combined.Height, 0.001);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_MergesWrappedSentence()
    {
        var lines = new List<TranslationLine>
        {
            new("This is a long sentence that", new PhysicalRect(100, 100, 420, 24), 0.9),
            new("wraps onto another line.", new PhysicalRect(102, 130, 330, 24), 0.8),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(1, grouped);
        Assert.AreEqual("This is a long sentence that wraps onto another line.", grouped[0].Text);
        Assert.AreEqual(2, grouped[0].SourceLineCount);
        Assert.AreEqual(100.0, grouped[0].BoundingBox.Left, 0.001);
        Assert.AreEqual(154.0, grouped[0].BoundingBox.Bottom, 0.001);
        Assert.AreEqual(0.85, grouped[0].Confidence, 0.001);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_KeepsDifferentFontSizeLinesSeparate()
    {
        var lines = new List<TranslationLine>
        {
            new("Large heading", new PhysicalRect(100, 100, 260, 36), 0.9),
            new("Smaller body text", new PhysicalRect(102, 144, 320, 20), 0.9),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(2, grouped);
        Assert.IsTrue(grouped.Any(line => line.Text == "Large heading"));
        Assert.IsTrue(grouped.Any(line => line.Text == "Smaller body text"));
    }

    [TestMethod]
    public void GroupAdjacentTextLines_MergesMinorLineHeightVariation()
    {
        var lines = new List<TranslationLine>
        {
            new("First wrapped line", new PhysicalRect(100, 100, 260, 24), 0.9),
            new("Second wrapped line", new PhysicalRect(102, 130, 280, 22), 0.9),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(1, grouped);
        Assert.AreEqual("First wrapped line Second wrapped line", grouped[0].Text);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_MergesSameBaselineFragmentsWithPunctuation()
    {
        var lines = new List<TranslationLine>
        {
            new("This is one sentence", new PhysicalRect(100, 100, 230, 24)),
            new(".", new PhysicalRect(334, 101, 7, 23)),
            new("It continues here", new PhysicalRect(350, 100, 180, 24)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(1, grouped);
        Assert.AreEqual("This is one sentence. It continues here", grouped[0].Text);
        Assert.AreEqual(1, grouped[0].SourceLineCount);
        Assert.AreEqual(100.0, grouped[0].BoundingBox.Left, 0.001);
        Assert.AreEqual(530.0, grouped[0].BoundingBox.Right, 0.001);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_MergesInlineMarkdownStyles()
    {
        var lines = new List<TranslationLine>
        {
            new("Use the", new PhysicalRect(100, 100, 82, 24)),
            new("inline code", new PhysicalRect(205, 105, 118, 18)),
            new("option", new PhysicalRect(355, 96, 76, 30)),
            new("here.", new PhysicalRect(456, 101, 58, 23)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(1, grouped);
        Assert.AreEqual("Use the inline code option here.", grouped[0].Text);
        Assert.AreEqual(1, grouped[0].SourceLineCount);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_MergesRaisedInlineFragment()
    {
        var lines = new List<TranslationLine>
        {
            new("Read the note", new PhysicalRect(100, 100, 130, 24)),
            new("1", new PhysicalRect(238, 89, 9, 14)),
            new("before continuing.", new PhysicalRect(255, 100, 180, 24)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(1, grouped);
        Assert.AreEqual("Read the note 1 before continuing.", grouped[0].Text);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_KeepsLargeSameBaselineGapSeparate()
    {
        var lines = new List<TranslationLine>
        {
            new("Left label", new PhysicalRect(100, 100, 120, 24)),
            new("Right label", new PhysicalRect(600, 100, 130, 24)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(2, grouped);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_KeepsSeparateColumnsApart()
    {
        var lines = new List<TranslationLine>
        {
            new("Left column", new PhysicalRect(100, 100, 180, 24)),
            new("Right column", new PhysicalRect(600, 101, 180, 24)),
            new("Left continuation", new PhysicalRect(102, 132, 210, 24)),
            new("Right continuation", new PhysicalRect(602, 133, 220, 24)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(2, grouped);
        Assert.IsTrue(grouped.Any(line => line.Text == "Left column Left continuation"));
        Assert.IsTrue(grouped.Any(line => line.Text == "Right column Right continuation"));
    }

    [TestMethod]
    public void GroupAdjacentTextLines_JoinsHyphenatedLineWithoutSpace()
    {
        var lines = new List<TranslationLine>
        {
            new("multi-", new PhysicalRect(100, 100, 80, 20)),
            new("line", new PhysicalRect(100, 125, 60, 20)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(lines);

        Assert.HasCount(1, grouped);
        Assert.AreEqual("multiline", grouped[0].Text);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_Scored_KeepsCompletedStatementsSeparate()
    {
        var lines = new List<TranslationLine>
        {
            new("First status is complete.", new PhysicalRect(100, 100, 280, 24)),
            new("Second status is pending.", new PhysicalRect(100, 130, 280, 24)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(
            lines,
            TextBlockGroupingStrategy.Scored);

        Assert.HasCount(2, grouped);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_Scored_KeepsTerminalPromptsSeparate()
    {
        var lines = new List<TranslationLine>
        {
            new("> first command", new PhysicalRect(100, 100, 220, 24)),
            new("> second command", new PhysicalRect(100, 130, 230, 24)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(
            lines,
            TextBlockGroupingStrategy.Scored);

        Assert.HasCount(2, grouped);
    }

    [TestMethod]
    public void GroupAdjacentTextLines_LegacyStrategyRemainsAvailable()
    {
        var lines = new List<TranslationLine>
        {
            new("First status is complete.", new PhysicalRect(100, 100, 280, 24)),
            new("Second status is pending.", new PhysicalRect(100, 130, 280, 24)),
        };

        IReadOnlyList<TranslationLine> grouped = OverlayLayoutHelper.GroupAdjacentTextLines(
            lines,
            TextBlockGroupingStrategy.Legacy);

        Assert.HasCount(1, grouped);
    }

    [TestMethod]
    public void ClampToScreen_ClampsOutOfBoundsRect()
    {
        PhysicalRect screen = new(0, 0, 1920, 1080);
        PhysicalRect outOfBounds = new(-50, -20, 200, 100);

        var clamped = OverlayLayoutHelper.ClampToScreen(outOfBounds, screen);

        Assert.AreEqual(0.0, clamped.Left, 0.001);
        Assert.AreEqual(0.0, clamped.Top, 0.001);
        Assert.AreEqual(150.0, clamped.Right, 0.001);
        Assert.AreEqual(80.0, clamped.Bottom, 0.001);
    }

    [TestMethod]
    public void SanitizeTranslatedLineGeometry_LeavesNormalBoxesUnchanged()
    {
        PhysicalRect capture = new(100, 100, 800, 600);
        var lines = new List<TranslatedLine>
        {
            new("Hello", "こんにちは", new PhysicalRect(140, 160, 220, 32), 0.9),
        };

        IReadOnlyList<TranslatedLine> sanitized = OverlayLayoutHelper.SanitizeTranslatedLineGeometry(lines, capture);

        Assert.HasCount(1, sanitized);
        Assert.AreEqual(lines[0].BoundingBox, sanitized[0].BoundingBox);
    }

    [TestMethod]
    public void SanitizeTranslatedLineGeometry_RepairsNonFiniteBoxes()
    {
        PhysicalRect capture = new(100, 100, 800, 600);
        var lines = new List<TranslatedLine>
        {
            new("Broken", "壊れた", new PhysicalRect(double.NaN, 120, double.PositiveInfinity, 40), 0.5),
        };

        IReadOnlyList<TranslatedLine> sanitized = OverlayLayoutHelper.SanitizeTranslatedLineGeometry(lines, capture);

        Assert.HasCount(1, sanitized);
        Assert.IsTrue(double.IsFinite(sanitized[0].BoundingBox.Left));
        Assert.IsTrue(double.IsFinite(sanitized[0].BoundingBox.Width));
        Assert.IsTrue(capture.Contains(new PhysicalPoint(sanitized[0].BoundingBox.Left, sanitized[0].BoundingBox.Top)));
        Assert.IsTrue(sanitized[0].BoundingBox.Right <= capture.Right);
        Assert.IsTrue(sanitized[0].BoundingBox.Bottom <= capture.Bottom);
    }

    [TestMethod]
    public void SanitizeTranslatedLineGeometry_ClampsOversizedBoxesToCapture()
    {
        PhysicalRect capture = new(100, 100, 800, 600);
        var lines = new List<TranslatedLine>
        {
            new("Huge", "巨大", new PhysicalRect(-500, -400, 4000, 3000), 0.5),
        };

        IReadOnlyList<TranslatedLine> sanitized = OverlayLayoutHelper.SanitizeTranslatedLineGeometry(lines, capture);

        Assert.HasCount(1, sanitized);
        Assert.AreEqual(capture.Left, sanitized[0].BoundingBox.Left, 0.001);
        Assert.AreEqual(capture.Top, sanitized[0].BoundingBox.Top, 0.001);
        Assert.IsTrue(sanitized[0].BoundingBox.Width <= capture.Width * 0.9);
        Assert.IsTrue(sanitized[0].BoundingBox.Height <= capture.Height * 0.35);
    }

    [TestMethod]
    public void SanitizeOcrLineGeometry_ClampsWordGeometryButKeepsNormalWords()
    {
        PhysicalRect capture = new(0, 0, 400, 200);
        RecognizedWord normal = new("normal", new PhysicalRect(20, 30, 60, 20), 0, 0);
        RecognizedWord oversized = new("oversized", new PhysicalRect(-10, 40, 450, 30), 0, 1);
        var lines = new List<TranslationLine>
        {
            new("normal oversized", new PhysicalRect(20, 30, 380, 40), 0.9, Words: new[] { normal, oversized }),
        };

        IReadOnlyList<TranslationLine> sanitized = OverlayLayoutHelper.SanitizeOcrLineGeometry(lines, capture);

        Assert.HasCount(1, sanitized);
        Assert.IsNotNull(sanitized[0].Words);
        Assert.HasCount(2, sanitized[0].Words!);
        Assert.AreEqual(normal.BoundingBox, sanitized[0].Words![0].BoundingBox);
        Assert.AreEqual(0.0, sanitized[0].Words![1].BoundingBox.Left, 0.001);
        Assert.AreEqual(400.0, sanitized[0].Words![1].BoundingBox.Right, 0.001);
    }

    [TestMethod]
    public void CalculateInitialCardSize_CapsMalformedNearFullscreenDefaults()
    {
        var (width, minHeight) = OverlayLayoutHelper.CalculateInitialCardSize(
            widthDip: 1800,
            heightDip: 900,
            captureWidthDip: 1920,
            captureHeightDip: 1080,
            sourceLineCount: 1);

        Assert.AreEqual(1728.0, width, 0.001);
        Assert.AreEqual(378.0, minHeight, 0.001);
    }

    [TestMethod]
    public void CalculateInitialCardSize_LeavesNormalBoxesUnchanged()
    {
        var (width, minHeight) = OverlayLayoutHelper.CalculateInitialCardSize(
            widthDip: 240,
            heightDip: 32,
            captureWidthDip: 1920,
            captureHeightDip: 1080,
            sourceLineCount: 1);

        Assert.AreEqual(240.0, width, 0.001);
        Assert.AreEqual(32.0, minHeight, 0.001);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_ExpandsLongEnglishTitleAndPreservesHeight()
    {
        PhysicalRect source = new(80, 40, 180, 36);
        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Powerful productivity for everyone",
                source,
                [source],
                captureRight: 900),
            measuredDesiredWidth: 300,
            measuredReducedWidth: 260);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsTrue(layout.FitsSingleLine);
        Assert.IsTrue(layout.Width > source.Width);
        Assert.AreEqual(36.0, layout.MinHeight, 0.001);
        Assert.AreEqual(30.6, layout.FontSize, 0.001);
        Assert.IsTrue(layout.Width <= 820.0);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_RightNeighborLimitsExpansion()
    {
        PhysicalRect source = new(80, 40, 180, 36);
        PhysicalRect neighbor = new(430, 42, 180, 24);
        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Longer English title",
                source,
                [source, neighbor],
                captureRight: 900),
            measuredDesiredWidth: 400,
            measuredReducedWidth: 260);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsTrue(layout.Width <= 342.0);
        Assert.IsTrue(source.Left + layout.Width <= neighbor.Left - 8.0);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_InsufficientWidthReducesFontThenWraps()
    {
        PhysicalRect source = new(80, 40, 180, 36);
        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "A substantially longer English title that cannot fit",
                source,
                [source],
                captureRight: 355),
            measuredDesiredWidth: 400,
            measuredReducedWidth: 300);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsFalse(layout.FitsSingleLine);
        Assert.AreEqual(275.0, layout.Width, 0.001);
        Assert.AreEqual(25.092, layout.FontSize, 0.001);
        Assert.AreEqual(36.0, layout.MinHeight, 0.001);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_BodyAndMultilineRemainUnchanged()
    {
        PhysicalRect body = new(80, 40, 180, 20);
        OverlayLayoutHelper.AdaptiveCardLayout bodyLayout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput("Long body text that would otherwise expand", body, [body], captureRight: 900),
            measuredDesiredWidth: 300,
            measuredReducedWidth: 260);
        PhysicalRect multiline = new(80, 80, 240, 64);
        OverlayLayoutHelper.AdaptiveCardLayout multilineLayout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Long translated multiline group",
                multiline,
                [multiline],
                captureRight: 900,
                sourceLineCount: 2),
            measuredDesiredWidth: 300,
            measuredReducedWidth: 260);

        Assert.IsFalse(bodyLayout.IsAdapted);
        Assert.AreEqual(body.Width, bodyLayout.Width, 0.001);
        Assert.IsFalse(multilineLayout.IsAdapted);
        Assert.AreEqual(multiline.Width, multilineLayout.Width, 0.001);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_BodyLinesOfSimilarHeightRemainUnchanged()
    {
        PhysicalRect body = new(80, 40, 240, 30);
        OverlayLayoutHelper.AdaptiveCardLayoutInput input = CreateAdaptiveInput(
            "A body line",
            body,
            [body, new PhysicalRect(80, 80, 240, 28), new PhysicalRect(80, 120, 240, 30)],
            captureRight: 900);

        Assert.IsFalse(OverlayLayoutHelper.IsAdaptiveTitleCandidate(input));
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_ClearlyLargerLineQualifiesAsTitle()
    {
        PhysicalRect title = new(80, 40, 240, 40);
        OverlayLayoutHelper.AdaptiveCardLayoutInput input = CreateAdaptiveInput(
            "A clearly larger title",
            title,
            [title, new PhysicalRect(80, 100, 240, 28), new PhysicalRect(80, 140, 240, 30)],
            captureRight: 900);

        Assert.IsTrue(OverlayLayoutHelper.IsAdaptiveTitleCandidate(input));
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_HighDpiChineseTitleQualifiesAndExpands()
    {
        PhysicalRect title = new(135, 72, 140, 24);
        IReadOnlyList<PhysicalRect> allBounds =
        [
            title,
            new PhysicalRect(135, 36, 190, 14),
            new PhysicalRect(163, 157, 100, 16),
            new PhysicalRect(163, 197, 170, 20),
            new PhysicalRect(163, 405, 170, 14),
        ];

        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Real-time monitoring",
                title,
                allBounds,
                captureRight: 1600),
            measuredDesiredWidth: 245,
            measuredReducedWidth: 205);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsTrue(layout.FitsSingleLine);
        Assert.IsTrue(layout.Width > title.Width);
        Assert.AreEqual(title.Height, layout.MinHeight, 0.001);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_CompactChineseCardHeadingExpands()
    {
        PhysicalRect heading = new(163, 306, 100, 16);
        IReadOnlyList<PhysicalRect> allBounds =
        [
            heading,
            new PhysicalRect(163, 348, 128, 34),
            new PhysicalRect(163, 405, 170, 14),
            new PhysicalRect(597, 306, 90, 16),
        ];

        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Affected orders",
                heading,
                allBounds,
                captureRight: 1600,
                sourceText: "受影响订单"),
            measuredDesiredWidth: 118,
            measuredReducedWidth: 102);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsTrue(layout.FitsSingleLine);
        Assert.IsTrue(layout.Width > heading.Width);
        Assert.AreEqual(heading.Height, layout.MinHeight, 0.001);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_CompactChineseHeadingCapsInflatedFont()
    {
        PhysicalRect heading = new(744, 710, 172, 34);
        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Order submission failed",
                heading,
                [heading, new PhysicalRect(744, 756, 150, 16)],
                captureRight: 1600,
                sourceText: "订单提交失败"),
            measuredDesiredWidth: 325,
            measuredReducedWidth: 205);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsTrue(layout.FitsSingleLine);
        Assert.AreEqual(18.0, layout.FontSize, 0.001);
        Assert.IsTrue(layout.Width > heading.Width);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_CompactHeadingKeepsFontCapWhenWidthCannotExpand()
    {
        PhysicalRect heading = new(100, 100, 172, 34);
        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Order submission failed",
                heading,
                [
                    heading,
                    new PhysicalRect(40, 102, 52, 24),
                    new PhysicalRect(280, 102, 80, 24),
                ],
                captureRight: 400,
                sourceText: "订单提交失败"),
            measuredDesiredWidth: 325,
            measuredReducedWidth: 205);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsFalse(layout.FitsSingleLine);
        Assert.AreEqual(18.0, layout.FontSize, 0.001);
        Assert.AreEqual(heading.Width, layout.Width, 0.001);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_CompactLatinBodyLabelRemainsUnchanged()
    {
        PhysicalRect label = new(163, 306, 100, 16);
        OverlayLayoutHelper.AdaptiveCardLayoutInput input = CreateAdaptiveInput(
            "Affected orders",
            label,
            [label, new PhysicalRect(163, 348, 128, 16)],
            captureRight: 1600,
            sourceText: "Affected orders");

        Assert.IsFalse(OverlayLayoutHelper.IsAdaptiveTitleCandidate(input));
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_RtlExpandsLeftAndStopsAtLeftNeighbor()
    {
        PhysicalRect source = new(400, 40, 180, 36);
        PhysicalRect neighbor = new(180, 42, 120, 24);
        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "عنوان عربي طويل",
                source,
                [source, neighbor],
                captureRight: 900,
                captureLeft: 0,
                overlayLeft: 0),
            measuredDesiredWidth: 400,
            measuredReducedWidth: 250);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsTrue(layout.FitsSingleLine);
        Assert.AreEqual(264.0, layout.Width, 0.001);
        Assert.AreEqual(316.0, layout.Left, 0.001);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_UsesLargerLeftSpanWhenRightIsBlocked()
    {
        PhysicalRect source = new(500, 40, 180, 36);
        PhysicalRect rightNeighbor = new(710, 42, 120, 24);
        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Long translated heading",
                source,
                [source, rightNeighbor],
                captureRight: 900,
                captureLeft: 0,
                overlayLeft: 0,
                sourceText: "长标题"),
            measuredDesiredWidth: 350,
            measuredReducedWidth: 280);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsTrue(layout.Left < source.Left);
        Assert.IsTrue(layout.Left + layout.Width <= source.Right + 0.001);
    }

    [TestMethod]
    public void CalculateNonOverlappingVerticalPlacement_PrefersDownwardSpace()
    {
        PhysicalRect source = new(100, 100, 220, 24);
        OverlayLayoutHelper.VerticalCardPlacement placement =
            OverlayLayoutHelper.CalculateNonOverlappingVerticalPlacement(
                source,
                [source, new PhysicalRect(100, 190, 220, 24)],
                sourceIndex: 0,
                cardLeft: 100,
                cardWidth: 220,
                desiredHeight: 58,
                captureTop: 0,
                captureBottom: 500);

        Assert.IsTrue(placement.FitsWithoutOverlap);
        Assert.AreEqual(100.0, placement.Top, 0.001);
        Assert.AreEqual(58.0, placement.MinHeight, 0.001);
    }

    [TestMethod]
    public void CalculateNonOverlappingVerticalPlacement_ExpandsUpWhenBottomIsBlocked()
    {
        PhysicalRect source = new(100, 100, 220, 24);
        OverlayLayoutHelper.VerticalCardPlacement placement =
            OverlayLayoutHelper.CalculateNonOverlappingVerticalPlacement(
                source,
                [
                    source,
                    new PhysicalRect(100, 20, 220, 20),
                    new PhysicalRect(100, 130, 220, 24),
                ],
                sourceIndex: 0,
                cardLeft: 100,
                cardWidth: 220,
                desiredHeight: 52,
                captureTop: 0,
                captureBottom: 500);

        Assert.IsTrue(placement.FitsWithoutOverlap);
        Assert.AreEqual(72.0, placement.Top, 0.001);
    }

    [TestMethod]
    public void CalculateAdaptiveInitialCardLayout_StopsBeforePreviouslyExpandedCard()
    {
        PhysicalRect source = new(500, 100, 110, 18);
        PhysicalRect expandedCard = new(260, 98, 220, 28);
        OverlayLayoutHelper.AdaptiveCardLayout layout = OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
            CreateAdaptiveInput(
                "Current processing status",
                source,
                [expandedCard, source],
                captureRight: 900,
                sourceText: "处理状态",
                sourceIndex: 1),
            measuredDesiredWidth: 230,
            measuredReducedWidth: 190);

        Assert.IsTrue(layout.IsAdapted);
        Assert.IsTrue(layout.Left >= expandedCard.Right + 8.0);
    }

    [TestMethod]
    public void FindContainingScreen_IdentifiesCorrectScreen()
    {
        var screens = new List<PhysicalRect>
        {
            new(0, 0, 1920, 1080),
            new(1920, 0, 2560, 1440),
        };

        var screen1 = OverlayLayoutHelper.FindContainingScreen(new PhysicalPoint(500, 500), screens);
        Assert.IsNotNull(screen1);
        Assert.AreEqual(0.0, screen1.Value.X);

        var screen2 = OverlayLayoutHelper.FindContainingScreen(new PhysicalPoint(2500, 500), screens);
        Assert.IsNotNull(screen2);
        Assert.AreEqual(1920.0, screen2.Value.X);
    }

    private static OverlayLayoutHelper.AdaptiveCardLayoutInput CreateAdaptiveInput(
        string text,
        PhysicalRect source,
        IReadOnlyList<PhysicalRect> allBounds,
        double captureRight,
        int sourceLineCount = 1,
        double captureLeft = 0,
        double overlayLeft = 0,
        string? sourceText = null,
        int sourceIndex = 0)
    {
        double sourceLineHeight = source.Height / sourceLineCount;
        return new OverlayLayoutHelper.AdaptiveCardLayoutInput(
            text,
            sourceText ?? text,
            source,
            allBounds,
            allBounds.Select((bounds, index) => index == sourceIndex ? sourceLineCount : 1).ToList(),
            sourceIndex,
            sourceLineCount,
            InitialWidth: source.Width,
            InitialMinHeight: source.Height,
            FontSize: OverlayLayoutHelper.CalculateEstimatedFontSize(sourceLineHeight),
            CaptureLeft: captureLeft,
            captureRight,
            overlayLeft,
            OverlayRight: 1000);
    }
}
