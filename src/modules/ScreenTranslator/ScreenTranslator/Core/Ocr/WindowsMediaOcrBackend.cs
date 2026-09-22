// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedCommon;
using ScreenTranslator.Core.Layout;
using ScreenTranslator.Core.Translation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenTranslator.Core.Ocr;

/// <summary>
/// Universal fallback OCR backend using Windows.Media.Ocr.OcrEngine.
/// Available across all Windows 10/11 installations.
/// </summary>
public sealed class WindowsMediaOcrBackend : IOcrBackend
{
    public string BackendName => "Windows.Media.Ocr (Standard/Fallback)";

    public bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    public async Task<IReadOnlyList<TranslationLine>> RecognizeTextAsync(
        SoftwareBitmap bitmap,
        PhysicalRect capturedRegionPhysical,
        string? sourceLanguageTag = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return Array.Empty<TranslationLine>();
        }

        bool convertedLocally = false;
        SoftwareBitmap convertedBitmap;
        if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && bitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied)
        {
            convertedBitmap = bitmap;
        }
        else
        {
            convertedBitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            convertedLocally = true;
        }

        try
        {
            if (IsAutomaticLanguage(sourceLanguageTag))
            {
                return await RecognizeAutomaticLanguageAsync(convertedBitmap, capturedRegionPhysical, cancellationToken);
            }

            Language language = ResolveExplicitLanguage(sourceLanguageTag);
            IReadOnlyList<string> relatedCjkCandidates = OcrLanguageSelectionHelper.GetRelatedCjkLanguageCandidates(
                OcrEngine.AvailableRecognizerLanguages.Select(candidate => candidate.LanguageTag),
                language.LanguageTag);
            if (relatedCjkCandidates.Count > 1)
            {
                return await RecognizeBestLanguageAsync(
                    convertedBitmap,
                    capturedRegionPhysical,
                    relatedCjkCandidates,
                    $"preferred CJK language '{language.LanguageTag}'",
                    cancellationToken);
            }

            OcrEngine? engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine == null)
            {
                throw new InvalidOperationException($"Windows OCR could not be created for language '{language.LanguageTag}'.");
            }

            IReadOnlyList<TranslationLine> lines = await RecognizeWithEngineAsync(
                engine,
                convertedBitmap,
                capturedRegionPhysical,
                language.LanguageTag,
                cancellationToken);
            Logger.LogInfo($"Windows OCR selected language '{language.LanguageTag}' for recognition.");
            return lines;
        }
        finally
        {
            if (convertedLocally)
            {
                convertedBitmap.Dispose();
            }
        }
    }

    private static async Task<IReadOnlyList<TranslationLine>> RecognizeAutomaticLanguageAsync(
        SoftwareBitmap bitmap,
        PhysicalRect capturedRegionPhysical,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> candidateTags = GetAutomaticLanguageCandidates();
        if (candidateTags.Count == 0)
        {
            throw new InvalidOperationException("Windows OCR could not be created because no supported OCR language is installed.");
        }

        return await RecognizeBestLanguageAsync(
            bitmap,
            capturedRegionPhysical,
            candidateTags,
            "Auto recognition",
            cancellationToken);
    }

    private static async Task<IReadOnlyList<TranslationLine>> RecognizeBestLanguageAsync(
        SoftwareBitmap bitmap,
        PhysicalRect capturedRegionPhysical,
        IReadOnlyList<string> candidateTags,
        string selectionReason,
        CancellationToken cancellationToken)
    {
        List<(string LanguageTag, IReadOnlyList<TranslationLine> Lines, OcrLanguageScore Score)> recognized = new();
        for (int index = 0; index < candidateTags.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string candidateTag = candidateTags[index];
            OcrEngine? engine = OcrEngine.TryCreateFromLanguage(new Language(candidateTag));
            if (engine == null)
            {
                continue;
            }

            IReadOnlyList<TranslationLine> lines = await RecognizeWithEngineAsync(
                engine,
                bitmap,
                capturedRegionPhysical,
                candidateTag,
                cancellationToken);
            OcrLanguageScore score = OcrLanguageSelectionHelper.ScoreRecognizedLines(candidateTag, lines, index);
            recognized.Add((candidateTag, lines, score));
        }

        if (recognized.Count == 0)
        {
            throw new InvalidOperationException("Windows OCR could not be created because no supported OCR language is installed.");
        }

        OcrLanguageScore selectedScore = OcrLanguageSelectionHelper.SelectBestScore(
            recognized.Select(candidate => candidate.Score));
        var selected = recognized.First(candidate => string.Equals(
            candidate.LanguageTag,
            selectedScore.LanguageTag,
            StringComparison.OrdinalIgnoreCase));
        Logger.LogInfo(
            $"Windows OCR selected language '{selected.LanguageTag}' from {recognized.Count} candidate(s) for {selectionReason}.");
        return selected.Lines;
    }

    private static async Task<IReadOnlyList<TranslationLine>> RecognizeWithEngineAsync(
        OcrEngine engine,
        SoftwareBitmap bitmap,
        PhysicalRect capturedRegionPhysical,
        string languageTag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OcrResult result = await engine.RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        return ConvertResultToLines(result, capturedRegionPhysical, languageTag);
    }

    private static IReadOnlyList<TranslationLine> ConvertResultToLines(
        OcrResult result,
        PhysicalRect capturedRegionPhysical,
        string languageTag)
    {
        if (result == null || result.Lines == null || result.Lines.Count == 0)
        {
            return Array.Empty<TranslationLine>();
        }

        List<TranslationLine> lines = new();

        for (int lineIndex = 0; lineIndex < result.Lines.Count; lineIndex++)
        {
            OcrLine ocrLine = result.Lines[lineIndex];
            if (string.IsNullOrWhiteSpace(ocrLine.Text))
            {
                continue;
            }

            List<PhysicalRect> wordRects = new();
            List<RecognizedWord> recognizedWords = new();
            for (int wordIndex = 0; wordIndex < ocrLine.Words.Count; wordIndex++)
            {
                OcrWord word = ocrLine.Words[wordIndex];
                PhysicalRect wordRect = new(
                    capturedRegionPhysical.X + word.BoundingRect.X,
                    capturedRegionPhysical.Y + word.BoundingRect.Y,
                    word.BoundingRect.Width,
                    word.BoundingRect.Height);
                wordRects.Add(wordRect);
                recognizedWords.Add(new RecognizedWord(word.Text, wordRect, lineIndex, wordIndex));
            }

            PhysicalRect lineBoundingBox = OverlayLayoutHelper.CombineWordRects(wordRects);
            if (lineBoundingBox.IsEmpty)
            {
                lineBoundingBox = new PhysicalRect(
                    capturedRegionPhysical.X,
                    capturedRegionPhysical.Y,
                    capturedRegionPhysical.Width,
                    capturedRegionPhysical.Height);
            }

            lines.Add(new TranslationLine(
                ocrLine.Text.Trim(),
                lineBoundingBox,
                1.0,
                null,
                Words: recognizedWords,
                RecognizedLanguageTag: languageTag));
        }

        return lines;
    }

    public static Language? ResolveLanguage(string? sourceLanguageTag)
    {
        return IsAutomaticLanguage(sourceLanguageTag)
            ? GetPreferredLanguage()
            : ResolveExplicitLanguage(sourceLanguageTag);
    }

    private static Language ResolveExplicitLanguage(string? sourceLanguageTag)
    {
        if (!string.IsNullOrWhiteSpace(sourceLanguageTag) &&
            !string.Equals(sourceLanguageTag, "auto", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sourceLanguageTag, "system", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var requestedLang = new Language(sourceLanguageTag.Trim());
                if (OcrEngine.IsLanguageSupported(requestedLang))
                {
                    return requestedLang;
                }

                throw new InvalidOperationException($"Windows OCR language '{sourceLanguageTag}' is not installed.");
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"OCR language tag '{sourceLanguageTag}' is invalid.", ex);
            }
        }

        Language? preferred = GetPreferredLanguage();
        if (preferred != null)
        {
            return preferred;
        }

        throw new InvalidOperationException("Windows OCR could not be created because no supported OCR language is installed.");
    }

    public static Language? GetPreferredLanguage()
    {
        try
        {
            var userLanguages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            if (userLanguages != null && userLanguages.Count > 0)
            {
                foreach (string langTag in userLanguages)
                {
                    Language lang = new(langTag);
                    if (OcrEngine.IsLanguageSupported(lang))
                    {
                        return lang;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Exception querying preferred globalization languages: {ex.Message}");
        }

        return OcrEngine.AvailableRecognizerLanguages.Count > 0 ? OcrEngine.AvailableRecognizerLanguages[0] : null;
    }

    private static IReadOnlyList<string> GetAutomaticLanguageCandidates()
    {
        return OcrLanguageSelectionHelper.GetAutoLanguageCandidates(
            OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag),
            GetUserPreferredLanguageTags());
    }

    private static IReadOnlyList<string> GetUserPreferredLanguageTags()
    {
        try
        {
            var userLanguages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            return userLanguages?.ToArray() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Exception querying preferred globalization languages: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static bool IsAutomaticLanguage(string? sourceLanguageTag)
    {
        return string.IsNullOrWhiteSpace(sourceLanguageTag) ||
               string.Equals(sourceLanguageTag, "auto", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceLanguageTag, "system", StringComparison.OrdinalIgnoreCase);
    }
}
