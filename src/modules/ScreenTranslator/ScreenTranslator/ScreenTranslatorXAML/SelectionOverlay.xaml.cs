// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ManagedCommon;
using Microsoft.PowerToys.Common.UI.Controls.Window;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScreenTranslator.Core.Capture;
using ScreenTranslator.Core.Layout;
using ScreenTranslator.Core.Ocr;
using ScreenTranslator.Core.Translation;
using ScreenTranslator.Helpers;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using WinUIEx;
using WindowManager = ScreenTranslator.Helpers.WindowManager;

namespace ScreenTranslator;

public sealed partial class SelectionOverlay : TransparentWindow
{
    private readonly ScreenInfo _screenInfo;
    private readonly ITranslationProvider _translationProvider;
    private readonly IOcrBackend? _ocrBackend;
    private readonly string _sourceLanguage;
    private readonly string _targetLanguage;
    private readonly string _secondaryTargetLanguage;
    private readonly PhysicalRect? _foregroundWindowBounds;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly bool _freezeCapturedContentByDefault;
    private readonly bool _showsSelectionUi;

    private bool _isSelecting;
    private Windows.Foundation.Point _startPoint;

    public SelectionOverlay(
        ScreenInfo screenInfo,
        ITranslationProvider? translationProvider = null,
        string sourceLanguage = "auto",
        string targetLanguage = "en-US",
        string secondaryTargetLanguage = "zh-Hans",
        PhysicalRect? foregroundWindowBounds = null,
        IOcrBackend? ocrBackend = null,
        bool freezeCapturedContentByDefault = false,
        bool showSelectionUi = true)
    {
        _screenInfo = screenInfo;
        _translationProvider = translationProvider ?? new PassthroughTranslationProvider();
        _ocrBackend = ocrBackend;
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage;
        _secondaryTargetLanguage = secondaryTargetLanguage;
        _foregroundWindowBounds = foregroundWindowBounds;
        _freezeCapturedContentByDefault = freezeCapturedContentByDefault;
        _showsSelectionUi = showSelectionUi;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        InitializeComponent();
        ActiveWindowButton.IsEnabled = _foregroundWindowBounds.HasValue;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        int style = OSInterop.GetWindowLong(hwnd, OSInterop.GwlStyle);
        _ = OSInterop.SetWindowLong(hwnd, OSInterop.GwlStyle, style & ~OSInterop.WsCaption & ~OSInterop.WsThickFrame);

        int exStyle = OSInterop.GetWindowLong(hwnd, OSInterop.GwlExStyle);
        _ = OSInterop.SetWindowLong(hwnd, OSInterop.GwlExStyle, exStyle | OSInterop.WsExNoActivate | OSInterop.WsExToolWindow | OSInterop.WsExTopMost);

        AppWindow.MoveAndResize(new RectInt32(
            (int)_screenInfo.Bounds.X,
            (int)_screenInfo.Bounds.Y,
            (int)_screenInfo.Bounds.Width,
            (int)_screenInfo.Bounds.Height));
        uint setWindowFlags = OSInterop.SwpNoActivate | OSInterop.SwpNoOwnerZOrder;
        if (showSelectionUi)
        {
            setWindowFlags |= OSInterop.SwpShowWindow;
        }

        _ = OSInterop.SetWindowPos(
            hwnd,
            OSInterop.HwndTopMost,
            (int)_screenInfo.Bounds.X,
            (int)_screenInfo.Bounds.Y,
            (int)_screenInfo.Bounds.Width,
            (int)_screenInfo.Bounds.Height,
            setWindowFlags);

        try
        {
            this.SetIsShownInSwitchers(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"SetIsShownInSwitchers failed: {ex.Message}");
        }
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        BeginFullScreenCapture();
    }

    private void ActiveWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_foregroundWindowBounds.HasValue)
        {
            return;
        }

        BeginActiveWindowCapture();
    }

    public async void BeginFullScreenCapture()
    {
        await BeginDirectCaptureAsync(_screenInfo.Bounds);
    }

    public async void BeginActiveWindowCapture()
    {
        if (!_foregroundWindowBounds.HasValue)
        {
            return;
        }

        await BeginDirectCaptureAsync(_foregroundWindowBounds.Value);
    }

    private async Task BeginDirectCaptureAsync(PhysicalRect captureBounds)
    {
        await PrepareSelectionUiForCaptureAsync();

        try
        {
            await ProcessCaptureAndTranslateAsync(captureBounds);
        }
        finally
        {
            Close();
        }
    }

    private void SelectionCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isSelecting = true;
        _startPoint = e.GetCurrentPoint(SelectionCanvas).Position;

        Canvas.SetLeft(SelectionRectangle, _startPoint.X);
        Canvas.SetTop(SelectionRectangle, _startPoint.Y);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        SelectionRectangle.Visibility = Visibility.Visible;

        CursorClipper.ClipCursorToRect(_screenInfo.Bounds);
        SelectionCanvas.CapturePointer(e.Pointer);
    }

    private void SelectionCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(SelectionCanvas).Position;

        double x = Math.Min(_startPoint.X, currentPoint.X);
        double y = Math.Min(_startPoint.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - _startPoint.X);
        double height = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    private async void SelectionCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        SelectionCanvas.ReleasePointerCapture(e.Pointer);
        CursorClipper.UnclipCursor();

        var endPoint = e.GetCurrentPoint(SelectionCanvas).Position;

        double leftDip = Math.Min(_startPoint.X, endPoint.X);
        double topDip = Math.Min(_startPoint.Y, endPoint.Y);
        double widthDip = Math.Abs(endPoint.X - _startPoint.X);
        double heightDip = Math.Abs(endPoint.Y - _startPoint.Y);

        if (widthDip < 8 || heightDip < 8)
        {
            WindowManager.CloseAllSelectionOverlays();
            Logger.LogInfo("Selection region too small; cancelling capture.");
            return;
        }

        PhysicalRect physicalRect = OverlayLayoutHelper.DipToPhysical(
            leftDip,
            topDip,
            widthDip,
            heightDip,
            _screenInfo.Bounds,
            _screenInfo.DpiScaleX,
            _screenInfo.DpiScaleY);

        physicalRect = OverlayLayoutHelper.ClampToScreen(physicalRect, _screenInfo.Bounds);

        await PrepareSelectionUiForCaptureAsync();

        try
        {
            await ProcessCaptureAndTranslateAsync(physicalRect);
        }
        finally
        {
            Close();
        }
    }

    private async Task PrepareSelectionUiForCaptureAsync()
    {
        WindowManager.CloseOtherSelectionOverlays(this);
        if (!_showsSelectionUi)
        {
            return;
        }

        AppWindow.Hide();
        await Task.Delay(100);
    }

    private async Task ProcessCaptureAndTranslateAsync(
        PhysicalRect capturedRegionPhysical,
        string? sourceLanguage = null,
        string? targetLanguage = null)
    {
        SoftwareBitmap? capturedBitmap = null;
        ProcessingOverlay? processingOverlay = null;
        using CancellationTokenSource cancellationTokenSource = new();
        sourceLanguage ??= _sourceLanguage;
        targetLanguage ??= _targetLanguage;

        try
        {
            Logger.LogInfo($"Capturing region: {capturedRegionPhysical.X},{capturedRegionPhysical.Y} {capturedRegionPhysical.Width}x{capturedRegionPhysical.Height}");
            capturedBitmap = ScreenCaptureHelper.CaptureRegion(capturedRegionPhysical);

            if (capturedBitmap == null)
            {
                Logger.LogWarning("Screen capture returned null.");
                return;
            }

            processingOverlay = WindowManager.ShowProcessingOverlay(capturedRegionPhysical, cancellationTokenSource.Cancel);
            processingOverlay.UpdateStatus("Recognizing text...");

            var recognizedLines = _ocrBackend is null
                ? await OcrEngineHelper.ExtractLinesWithGeometryAsync(
                    capturedBitmap,
                    capturedRegionPhysical,
                    sourceLanguage,
                    cancellationTokenSource.Token)
                : await OcrEngineHelper.ExtractLinesWithGeometryAsync(
                    _ocrBackend,
                    capturedBitmap,
                    capturedRegionPhysical,
                    sourceLanguage,
                    cancellationTokenSource.Token);
            Logger.LogInfo($"Recognized {recognizedLines.Count} text lines.");

            if (recognizedLines.Count == 0)
            {
                return;
            }

            recognizedLines = OverlayLayoutHelper.SanitizeOcrLineGeometry(recognizedLines, capturedRegionPhysical);
            Logger.LogInfo($"Sanitized recognized text geometry to {recognizedLines.Count} text lines.");
            if (recognizedLines.Count == 0)
            {
                return;
            }

            IReadOnlyList<TranslationLine> groupedLines = OverlayLayoutHelper.GroupAdjacentTextLines(
                recognizedLines,
                TextBlockGroupingStrategy.Scored);
            Logger.LogInfo($"Grouped recognized text into {groupedLines.Count} layout blocks.");

            processingOverlay.UpdateStatus("Translating text...");
            Logger.LogInfo($"Translating {groupedLines.Count} layout blocks with provider {_translationProvider.ProviderId}.");
            TranslationResult result = await TranslateLinesAsync(
                groupedLines,
                sourceLanguage,
                targetLanguage,
                _secondaryTargetLanguage,
                cancellationTokenSource.Token);

            if (!result.Success)
            {
                Logger.LogWarning($"Translation failed: {result.ErrorMessage}");
                if (result.Lines.Count == 0 && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    processingOverlay.ShowError(
                        "Translation unavailable",
                        BuildTranslationErrorMessage(result.ErrorMessage));
                    processingOverlay = null;
                    return;
                }
            }
            else
            {
                Logger.LogInfo($"Translation completed with provider {_translationProvider.ProviderId}.");
            }

            IReadOnlyList<TranslatedLine> styledLines = await Task.Run(
                () => OverlayAppearanceHelper.ApplySampledAppearance(
                    capturedBitmap,
                    capturedRegionPhysical,
                    result.Lines),
                cancellationTokenSource.Token);

            SoftwareBitmap capturedSnapshot = SoftwareBitmap.Copy(capturedBitmap);
            if (!_dispatcherQueue.TryEnqueue(() =>
            {
                WindowManager.ShowResultOverlay(
                    capturedRegionPhysical,
                    styledLines,
                    capturedSnapshot,
                    _freezeCapturedContentByDefault,
                    sourceLanguage,
                    targetLanguage,
                    _secondaryTargetLanguage,
                    (lines, newSource, newTarget, newSecondaryTarget) => TranslateLinesAsync(lines, newSource, newTarget, newSecondaryTarget),
                    TranslateLineAsync);
            }))
            {
                capturedSnapshot.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogInfo("Screen translation was cancelled.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error in ProcessCaptureAndTranslateAsync: {ex}");
        }
        finally
        {
            WindowManager.CloseProcessingOverlay(processingOverlay);
            capturedBitmap?.Dispose();
        }
    }

    private async Task<TranslationResult> TranslateLineAsync(
        TranslationLine line,
        string sourceLanguage,
        string targetLanguage,
        string secondaryTargetLanguage)
    {
        TranslationResult result = await TranslateLinesAsync(
            new[] { line },
            sourceLanguage,
            targetLanguage,
            secondaryTargetLanguage);
        return result;
    }

    private async Task<TranslationResult> TranslateLinesAsync(
        IReadOnlyList<TranslationLine> lines,
        string sourceLanguage,
        string targetLanguage,
        string? secondaryTargetLanguage = null,
        CancellationToken cancellationToken = default)
    {
        secondaryTargetLanguage ??= _secondaryTargetLanguage;
        string resolvedTargetLanguage = LanguageSelectionHelper.ResolveAutomaticTarget(
            lines,
            sourceLanguage,
            targetLanguage,
            secondaryTargetLanguage);
        if (!string.Equals(resolvedTargetLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInfo($"Auto language direction selected target '{resolvedTargetLanguage}' instead of '{targetLanguage}'.");
        }

        TranslationResult result = await _translationProvider.TranslateAsync(
            new TranslationRequest(lines, sourceLanguage, resolvedTargetLanguage),
            cancellationToken);
        result = TranslationQualityGuard.ReplaceDegenerateTranslations(result, out int replacementCount);
        if (replacementCount > 0)
        {
            Logger.LogWarning($"Suppressed {replacementCount} degenerate translation result(s) from provider {_translationProvider.ProviderId}.");
        }

        return result with
        {
            SourceLanguage = sourceLanguage,
            TargetLanguage = resolvedTargetLanguage,
        };
    }

    private string BuildTranslationErrorMessage(string? errorMessage)
    {
        string providerName = _translationProvider.DisplayName;
        string details = string.IsNullOrWhiteSpace(errorMessage)
            ? "The translation provider returned an unknown error."
            : errorMessage;

        if (providerName.Contains("LibreTranslate", StringComparison.OrdinalIgnoreCase) &&
            (details.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
             details.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            return $"Provider: {providerName}\n{details}\nStart the local LibreTranslate service or choose another provider in Screen Translator settings.";
        }

        return $"Provider: {providerName}\n{details}\nCheck Screen Translator settings or choose another provider.";
    }
}
