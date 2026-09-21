// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedCommon;
using Microsoft.PowerToys.Common.UI.Controls.Window;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ScreenTranslator.Core.Layout;
using ScreenTranslator.Core.Ocr;
using ScreenTranslator.Core.Selection;
using ScreenTranslator.Core.Translation;
using ScreenTranslator.Helpers;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;
using WinUIEx;
using WindowManager = ScreenTranslator.Helpers.WindowManager;

namespace ScreenTranslator;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The window disposes owned resources when it closes")]
public sealed partial class BrushSelectionOverlay : TransparentWindow
{
    private const double BrushRadiusDip = 6;

    private readonly ScreenInfo _screenInfo;
    private readonly PhysicalRect _capturedRegion;
    private readonly SoftwareBitmap _capturedBitmap;
    private readonly IOcrBackend _ocrBackend;
    private readonly ITranslationProvider _translationProvider;
    private readonly string _sourceLanguage;
    private readonly string _targetLanguage;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<PhysicalPoint> _currentStrokePoints = new();
    private readonly List<Polyline> _strokeVisuals = new();
    private readonly Dictionary<RecognizedWord, Rectangle> _wordHighlights = new();
    private PhysicalRect _overlayBounds;
    private double _dpiScaleX;
    private double _dpiScaleY;
    private IReadOnlyList<RecognizedWord> _selectableWords = Array.Empty<RecognizedWord>();
    private BrushSelectionAccumulator? _selectionAccumulator;
    private Polyline? _currentStrokePath;
    private bool _initialized;
    private bool _isBrushing;
    private bool _isBusy = true;

    public BrushSelectionOverlay(
        ScreenInfo screenInfo,
        SoftwareBitmap capturedBitmap,
        IOcrBackend ocrBackend,
        ITranslationProvider translationProvider,
        string sourceLanguage,
        string targetLanguage)
    {
        _screenInfo = screenInfo;
        _capturedRegion = screenInfo.Bounds;
        _overlayBounds = screenInfo.Bounds;
        _dpiScaleX = screenInfo.DpiScaleX;
        _dpiScaleY = screenInfo.DpiScaleY;
        _capturedBitmap = capturedBitmap;
        _ocrBackend = ocrBackend;
        _translationProvider = translationProvider;
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage;

        InitializeComponent();
        DismissOnFocusLost = false;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int style = OSInterop.GetWindowLong(hwnd, OSInterop.GwlStyle);
        _ = OSInterop.SetWindowLong(hwnd, OSInterop.GwlStyle, style & ~OSInterop.WsCaption & ~OSInterop.WsThickFrame);

        int exStyle = OSInterop.GetWindowLong(hwnd, OSInterop.GwlExStyle);
        _ = OSInterop.SetWindowLong(hwnd, OSInterop.GwlExStyle, exStyle | OSInterop.WsExToolWindow | OSInterop.WsExTopMost);

        AppWindow.MoveAndResize(new RectInt32(
            (int)_capturedRegion.X,
            (int)_capturedRegion.Y,
            (int)_capturedRegion.Width,
            (int)_capturedRegion.Height));
        _ = OSInterop.SetWindowPos(
            hwnd,
            OSInterop.HwndTopMost,
            (int)_capturedRegion.X,
            (int)_capturedRegion.Y,
            (int)_capturedRegion.Width,
            (int)_capturedRegion.Height,
            OSInterop.SwpNoOwnerZOrder | OSInterop.SwpShowWindow);

        try
        {
            this.SetIsShownInSwitchers(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"SetIsShownInSwitchers failed: {ex.Message}");
        }

        RootGrid.Loaded += RootGrid_Loaded;
        Closed += BrushSelectionOverlay_Closed;
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            OverlayCoordinateSpace coordinateSpace = OverlayCoordinateSpaceHelper.GetForClient(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                RootGrid,
                _overlayBounds,
                _dpiScaleX,
                _dpiScaleY);
            _overlayBounds = coordinateSpace.Bounds;
            _dpiScaleX = coordinateSpace.ScaleX;
            _dpiScaleY = coordinateSpace.ScaleY;

            await InitializeFrozenBackgroundAsync();
            IReadOnlyList<TranslationLine> recognizedLines = await _ocrBackend.RecognizeTextAsync(
                _capturedBitmap,
                _capturedRegion,
                _sourceLanguage,
                _cancellationTokenSource.Token);
            recognizedLines = OverlayLayoutHelper.SanitizeOcrLineGeometry(recognizedLines, _capturedRegion);
            _selectableWords = BrushPhraseBuilder.GetSelectableWords(recognizedLines);
            _selectionAccumulator = new BrushSelectionAccumulator(_selectableWords);
            Logger.LogInfo($"Scan text recognized {recognizedLines.Count} lines and {_selectableWords.Count} selectable words.");

            OcrProgressRing.IsActive = false;
            OcrProgressRing.Visibility = Visibility.Collapsed;
            _isBusy = false;
            UpdateActionButtonState();
            if (_selectableWords.Count == 0)
            {
                InstructionTitle.Text = "No text found";
                InstructionText.Text = "Press Esc to close, or activate Scan text again on another screen.";
            }
            else
            {
                InstructionTitle.Text = "Brush across text";
                InstructionText.Text = "Drag across OCR words. Release to keep selecting, then choose Translate. Press Esc to cancel.";
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogInfo("Scan text OCR was cancelled.");
        }
        catch (Exception ex)
        {
            _isBusy = false;
            OcrProgressRing.IsActive = false;
            OcrProgressRing.Visibility = Visibility.Collapsed;
            UpdateActionButtonState();
            InstructionTitle.Text = "Text recognition unavailable";
            InstructionText.Text = "Press Esc to close and check Screen Translator settings.";
            Logger.LogError($"Scan text OCR failed: {ex}");
        }
    }

    private async Task InitializeFrozenBackgroundAsync()
    {
        var imageSource = new SoftwareBitmapSource();
        await imageSource.SetBitmapAsync(_capturedBitmap);
        FrozenBackgroundImage.Source = imageSource;
    }

    private void BrushCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isBusy || _selectableWords.Count == 0 || _selectionAccumulator == null)
        {
            return;
        }

        _isBrushing = true;
        _currentStrokePoints.Clear();
        _currentStrokePath = CreateStrokePath();
        _strokeVisuals.Add(_currentStrokePath);
        BrushCanvas.Children.Add(_currentStrokePath);
        BrushCanvas.CapturePointer(e.Pointer);
        AddStrokePoint(e.GetCurrentPoint(BrushCanvas).Position);
        UpdateSelectedWords();
    }

    private void BrushCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isBrushing)
        {
            return;
        }

        AddStrokePoint(e.GetCurrentPoint(BrushCanvas).Position);
        UpdateSelectedWords();
    }

    private void BrushCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isBrushing)
        {
            return;
        }

        _isBrushing = false;
        BrushCanvas.ReleasePointerCapture(e.Pointer);
        AddStrokePoint(e.GetCurrentPoint(BrushCanvas).Position);
        IReadOnlyList<RecognizedWord> selectedWords = UpdateSelectedWords();
        _currentStrokePoints.Clear();
        _currentStrokePath = null;
        if (selectedWords.Count == 0)
        {
            InstructionTitle.Text = "No text selected";
            InstructionText.Text = "Brush through the highlighted bounds of one or more words, then choose Translate.";
            return;
        }

        UpdateSelectionFeedback(selectedWords.Count);
    }

    private void AddStrokePoint(Windows.Foundation.Point pointDip)
    {
        _currentStrokePath?.Points.Add(pointDip);
        _currentStrokePoints.Add(new PhysicalPoint(
            _overlayBounds.X + (pointDip.X * _dpiScaleX),
            _overlayBounds.Y + (pointDip.Y * _dpiScaleY)));
    }

    private IReadOnlyList<RecognizedWord> UpdateSelectedWords()
    {
        if (_selectionAccumulator == null)
        {
            return Array.Empty<RecognizedWord>();
        }

        IReadOnlyList<RecognizedWord> selectedWords = _selectionAccumulator.AddStroke(
            _currentStrokePoints,
            BrushRadiusDip * Math.Max(_dpiScaleX, _dpiScaleY));
        HashSet<RecognizedWord> selectedSet = selectedWords.ToHashSet();

        foreach (RecognizedWord word in selectedWords)
        {
            if (_wordHighlights.ContainsKey(word))
            {
                continue;
            }

            Rectangle highlight = CreateWordHighlight(word.BoundingBox);
            _wordHighlights[word] = highlight;
            BrushCanvas.Children.Insert(0, highlight);
        }

        foreach (RecognizedWord word in _wordHighlights.Keys.Where(word => !selectedSet.Contains(word)).ToArray())
        {
            BrushCanvas.Children.Remove(_wordHighlights[word]);
            _wordHighlights.Remove(word);
        }

        UpdateActionButtonState();
        return selectedWords;
    }

    private Polyline CreateStrokePath()
    {
        return new Polyline
        {
            IsHitTestVisible = false,
            Opacity = 0.58,
            Stroke = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeThickness = 12,
        };
    }

    private Rectangle CreateWordHighlight(PhysicalRect bounds)
    {
        var (left, top, width, height) = OverlayLayoutHelper.PhysicalToDip(
            bounds,
            _overlayBounds,
            _dpiScaleX,
            _dpiScaleY);
        Rectangle highlight = new()
        {
            Width = width,
            Height = height,
            Fill = (Brush)Application.Current.Resources["AccentFillColorSecondaryBrush"],
            Opacity = 0.48,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(highlight, left);
        Canvas.SetTop(highlight, top);
        return highlight;
    }

    private async Task TranslatePhraseAsync(BrushPhrase phrase)
    {
        _isBusy = true;
        UpdateActionButtonState();
        OcrProgressRing.Visibility = Visibility.Visible;
        OcrProgressRing.IsActive = true;
        InstructionTitle.Text = "Translating selection...";
        InstructionText.Text = phrase.Text;

        try
        {
            double confidence = phrase.Words.Average(word => word.Confidence);
            TranslationLine line = new(phrase.Text, phrase.BoundingBox, confidence, SourceLineCount: phrase.Words.Select(word => word.LineIndex).Distinct().Count());
            Logger.LogInfo($"Scan text translating {phrase.Words.Count} selected words with provider {_translationProvider.ProviderId}.");
            TranslationResult result = await _translationProvider.TranslateAsync(
                new TranslationRequest(new[] { line }, _sourceLanguage, _targetLanguage),
                _cancellationTokenSource.Token);
            result = TranslationQualityGuard.ReplaceDegenerateTranslations(result, out int replacementCount);
            if (replacementCount > 0)
            {
                Logger.LogWarning($"Scan text suppressed a degenerate translation from provider {_translationProvider.ProviderId}.");
            }

            if (result.Lines.Count == 0)
            {
                _isBusy = false;
                OcrProgressRing.IsActive = false;
                OcrProgressRing.Visibility = Visibility.Collapsed;
                UpdateActionButtonState();
                InstructionTitle.Text = "Translation unavailable";
                InstructionText.Text = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Choose Translate to retry, or Clear and brush another phrase."
                    : result.ErrorMessage;
                return;
            }

            if (result.Success)
            {
                Logger.LogInfo($"Scan text translation completed with provider {_translationProvider.ProviderId}.");
            }
            else
            {
                Logger.LogWarning($"Scan text provider returned partial results: {result.ErrorMessage}");
            }

            IReadOnlyList<TranslatedLine> styledLines = OverlayAppearanceHelper.ApplySampledAppearance(
                _capturedBitmap,
                _capturedRegion,
                result.Lines);
            SoftwareBitmap resultSnapshot = SoftwareBitmap.Copy(_capturedBitmap);
            Close();
            WindowManager.ShowResultOverlay(
                _capturedRegion,
                styledLines,
                resultSnapshot,
                freezeCapturedContent: true,
                sourceLanguage: _sourceLanguage,
                targetLanguage: _targetLanguage);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInfo("Scan text translation was cancelled.");
        }
        catch (Exception ex)
        {
            _isBusy = false;
            OcrProgressRing.IsActive = false;
            OcrProgressRing.Visibility = Visibility.Collapsed;
            UpdateActionButtonState();
            InstructionTitle.Text = "Translation unavailable";
            InstructionText.Text = "Choose Translate to retry, or Clear and brush another phrase.";
            Logger.LogError($"Scan text translation failed: {ex}");
        }
    }

    private void ClearSelectionVisuals()
    {
        _selectionAccumulator?.Clear();
        _currentStrokePoints.Clear();
        _currentStrokePath = null;
        foreach (Polyline strokeVisual in _strokeVisuals)
        {
            BrushCanvas.Children.Remove(strokeVisual);
        }

        _strokeVisuals.Clear();
        foreach (Rectangle highlight in _wordHighlights.Values)
        {
            BrushCanvas.Children.Remove(highlight);
        }

        _wordHighlights.Clear();
        UpdateActionButtonState();
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        await TranslateSelectedWordsAsync();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSelectionVisuals();
        InstructionTitle.Text = "Selection cleared";
        InstructionText.Text = "Brush across OCR words. Release to keep selecting, then choose Translate.";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void TranslateKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (sender.Modifiers != Windows.System.VirtualKeyModifiers.None && sender.Modifiers != Windows.System.VirtualKeyModifiers.Control)
        {
            return;
        }

        if (sender.Key is not VirtualKey.Enter)
        {
            return;
        }

        args.Handled = await TranslateSelectedWordsAsync();
    }

    private async Task<bool> TranslateSelectedWordsAsync()
    {
        if (_selectionAccumulator == null || _isBusy || _selectionAccumulator.Count == 0)
        {
            return false;
        }

        BrushPhrase? phrase = BrushPhraseBuilder.Build(_selectionAccumulator.SelectedWords);
        if (phrase == null)
        {
            InstructionTitle.Text = "No text selected";
            InstructionText.Text = "Brush through the highlighted bounds of one or more words, then choose Translate.";
            UpdateActionButtonState();
            return false;
        }

        await TranslatePhraseAsync(phrase);
        return true;
    }

    private void UpdateSelectionFeedback(int selectedWordCount)
    {
        InstructionTitle.Text = selectedWordCount == 1 ? "1 word selected" : $"{selectedWordCount} words selected";
        InstructionText.Text = "Continue brushing to add words, or choose Translate.";
    }

    private void UpdateActionButtonState()
    {
        bool hasSelection = _selectionAccumulator?.Count > 0;
        bool isEnabled = !_isBusy && hasSelection;
        TranslateButton.IsEnabled = isEnabled;
        ClearButton.IsEnabled = isEnabled;
    }

    private void BrushSelectionOverlay_Closed(object sender, WindowEventArgs args)
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _capturedBitmap.Dispose();
    }
}
