// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ManagedCommon;
using Microsoft.PowerToys.Common.UI.Controls.Window;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenTranslator.Core.Actions;
using ScreenTranslator.Core.Layout;
using ScreenTranslator.Core.Translation;
using ScreenTranslator.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using WinUIEx;

namespace ScreenTranslator;

public sealed partial class ResultOverlay : TransparentWindow
{
    private readonly PhysicalRect _capturedRegion;
    private readonly Func<IReadOnlyList<TranslationLine>, string, string, Task<TranslationResult>>? _retranslateAll;
    private readonly Func<TranslationLine, string, string, Task<TranslationResult>>? _retranslateLine;
    private readonly IntPtr _hwnd;
    private readonly TextShareService _textShareService = new();
    private readonly List<(PhysicalRect Bounds, Border Card)> _cardHitRegions = new();
    private readonly HashSet<int> _hiddenLineIndices = new();
    private readonly Dictionary<int, (
        string Text,
        Windows.UI.Color Background,
        Windows.UI.Color Foreground,
        double FontSize,
        double Left,
        double Top,
        double Width,
        double MinHeight)> _initialAppearance = new();

    private readonly Dictionary<int, string> _translatedTexts = new();
    private readonly Dictionary<int, string> _sourceTexts = new();
    private readonly Dictionary<int, Stack<(string Source, string Translated, bool ShowingOriginal)>> _textUndo = new();
    private readonly Dictionary<int, List<ResizeHandle>> _resizeHandles = new();
    private readonly HashSet<int> _showingOriginalText = new();

    private readonly DispatcherQueueTimer _windowSwitchTimer;
    private readonly IntPtr _sourceWindow;
    private readonly long _shownTimestamp = Environment.TickCount64;
    private IReadOnlyList<TranslatedLine> _lines;
    private string _sourceLanguage;
    private string _targetLanguage;
    private PhysicalRect _overlayBounds;
    private Border? _dragCard;
    private uint _dragPointerId;
    private Windows.Foundation.Point _dragStart;
    private double _dragLeft;
    private double _dragTop;
    private ResizeHandle? _activeResizeHandle;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeStartLeft;
    private double _resizeStartTop;
    private int _contextMenuLineIndex = -1;
    private TranslatedLine? _contextMenuLine;
    private Border? _contextMenuCard;
    private int _actionMenuLineIndex = -1;
    private Border? _actionMenuCard;
    private Uri? _actionMenuWebUri;
    private string? _actionMenuEmailAddress;
    private bool _isInitializingColorPickers;
    private TextBox? _editingTextBox;
    private Border? _editingCard;
    private string _editingOriginalText = string.Empty;
    private int _selectedLineIndex = -1;
    private bool _isToolbarDragging;
    private bool _toolbarWasManuallyPositioned;
    private bool _coordinateSpaceInitialized;
    private uint _toolbarPointerId;
    private Windows.Foundation.Point _toolbarDragStart;
    private Thickness _toolbarDragStartMargin;
    private bool _isContextMenuAboveCard;
    private double _dpiScaleX;
    private double _dpiScaleY;

    public ResultOverlay(
        ScreenInfo screenInfo,
        PhysicalRect capturedRegion,
        IReadOnlyList<TranslatedLine> lines,
        SoftwareBitmap capturedSnapshot,
        bool freezeCapturedContent,
        IntPtr sourceWindow,
        string sourceLanguage,
        string targetLanguage,
        Func<IReadOnlyList<TranslationLine>, string, string, Task<TranslationResult>>? retranslateAll = null,
        Func<TranslationLine, string, string, Task<TranslationResult>>? retranslateLine = null)
    {
        _overlayBounds = screenInfo.WorkingArea;
        _dpiScaleX = screenInfo.DpiScaleX;
        _dpiScaleY = screenInfo.DpiScaleY;
        _capturedRegion = capturedRegion;
        _lines = lines;
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage;
        _sourceWindow = sourceWindow;
        _retranslateAll = retranslateAll;
        _retranslateLine = retranslateLine;
        DismissOnFocusLost = false;

        InitializeComponent();
        RootGrid.Loaded += RootGrid_Loaded;
        InitializeExternalActionAvailability();
        Closed += (_, _) => _textShareService.Close();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        int style = OSInterop.GetWindowLong(_hwnd, OSInterop.GwlStyle);
        _ = OSInterop.SetWindowLong(_hwnd, OSInterop.GwlStyle, style & ~OSInterop.WsCaption & ~OSInterop.WsThickFrame);

        int exStyle = OSInterop.GetWindowLong(_hwnd, OSInterop.GwlExStyle);
        _ = OSInterop.SetWindowLong(_hwnd, OSInterop.GwlExStyle, exStyle | OSInterop.WsExNoActivate | OSInterop.WsExToolWindow | OSInterop.WsExTopMost);

        AppWindow.MoveAndResize(new RectInt32(
            (int)_overlayBounds.X,
            (int)_overlayBounds.Y,
            (int)_overlayBounds.Width,
            (int)_overlayBounds.Height));
        _ = OSInterop.SetWindowPos(
            _hwnd,
            OSInterop.HwndTopMost,
            (int)_overlayBounds.X,
            (int)_overlayBounds.Y,
            (int)_overlayBounds.Width,
            (int)_overlayBounds.Height,
            OSInterop.SwpNoActivate | OSInterop.SwpNoOwnerZOrder | OSInterop.SwpShowWindow);

        try
        {
            this.SetIsShownInSwitchers(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"SetIsShownInSwitchers failed: {ex.Message}");
        }

        Closed += ResultOverlay_Closed;
        _windowSwitchTimer = DispatcherQueue.CreateTimer();
        _windowSwitchTimer.Interval = TimeSpan.FromMilliseconds(400);
        _windowSwitchTimer.Tick += WindowSwitchTimer_Tick;
        _windowSwitchTimer.Start();

        RenderTranslatedBoxes();
        PositionCaptureRegionOutline();
        PositionToolbar();
        SelectLanguage(OverallSourceLanguageComboBox, sourceLanguage);
        SelectLanguage(OverallTargetLanguageComboBox, targetLanguage);
        _ = InitializeFrozenBackgroundAsync(capturedSnapshot, freezeCapturedContent);
    }

    private async Task InitializeFrozenBackgroundAsync(SoftwareBitmap capturedSnapshot, bool freezeCapturedContent)
    {
        SoftwareBitmap? convertedSnapshot = null;
        try
        {
            SoftwareBitmap bitmapSource = capturedSnapshot;
            if (capturedSnapshot.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                capturedSnapshot.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                convertedSnapshot = SoftwareBitmap.Convert(
                    capturedSnapshot,
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);
                bitmapSource = convertedSnapshot;
            }

            var imageSource = new SoftwareBitmapSource();
            await imageSource.SetBitmapAsync(bitmapSource);
            FrozenBackgroundImage.Source = imageSource;

            PositionFrozenBackground();
            FreezeContentToggleButton.IsChecked = freezeCapturedContent;
            FrozenBackgroundImage.Visibility = freezeCapturedContent ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to initialize frozen capture background: {ex}");
            FreezeContentToggleButton.IsEnabled = false;
        }
        finally
        {
            convertedSnapshot?.Dispose();
            capturedSnapshot.Dispose();
        }
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_coordinateSpaceInitialized)
        {
            return;
        }

        _coordinateSpaceInitialized = true;
        OverlayCoordinateSpace coordinateSpace = OverlayCoordinateSpaceHelper.GetForClient(
            _hwnd,
            RootGrid,
            _overlayBounds,
            _dpiScaleX,
            _dpiScaleY);
        _overlayBounds = coordinateSpace.Bounds;
        _dpiScaleX = coordinateSpace.ScaleX;
        _dpiScaleY = coordinateSpace.ScaleY;

        RenderTranslatedBoxes();
        PositionFrozenBackground();
        PositionCaptureRegionOutline();
        PositionToolbar();
    }

    private void PositionFrozenBackground()
    {
        var (leftDip, topDip, widthDip, heightDip) = OverlayLayoutHelper.PhysicalToDip(
            _capturedRegion,
            _overlayBounds,
            _dpiScaleX,
            _dpiScaleY);
        Canvas.SetLeft(FrozenBackgroundImage, leftDip);
        Canvas.SetTop(FrozenBackgroundImage, topDip);
        FrozenBackgroundImage.Width = widthDip;
        FrozenBackgroundImage.Height = heightDip;
    }

    private void FreezeContentToggleButton_Click(object sender, RoutedEventArgs e)
    {
        FrozenBackgroundImage.Visibility = FreezeContentToggleButton.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ResultOverlay_Closed(object sender, WindowEventArgs args)
    {
        _windowSwitchTimer.Stop();
        ScreenTranslator.Helpers.WindowManager.SetResultCardEditing(false);
    }

    public void UndoLastTextChange()
    {
        if (_selectedLineIndex < 0 ||
            !_textUndo.TryGetValue(_selectedLineIndex, out Stack<(string Source, string Translated, bool ShowingOriginal)>? history) ||
            history.Count == 0 ||
            _selectedLineIndex >= _cardHitRegions.Count)
        {
            return;
        }

        (string source, string translated, bool showingOriginal) = history.Pop();
        _sourceTexts[_selectedLineIndex] = source;
        _translatedTexts[_selectedLineIndex] = translated;
        if (_cardHitRegions[_selectedLineIndex].Card.Child is TextBlock textBlock)
        {
            textBlock.Text = showingOriginal ? source : translated;
            textBlock.InvalidateMeasure();
            _cardHitRegions[_selectedLineIndex].Card.InvalidateMeasure();
        }

        if (showingOriginal)
        {
            _showingOriginalText.Add(_selectedLineIndex);
        }
        else
        {
            _showingOriginalText.Remove(_selectedLineIndex);
        }

        if (_contextMenuLineIndex == _selectedLineIndex)
        {
            SetOriginalTextButtonState(!showingOriginal);
        }
    }

    private void PushTextUndo(int lineIndex)
    {
        if (!_textUndo.TryGetValue(lineIndex, out Stack<(string Source, string Translated, bool ShowingOriginal)>? history))
        {
            history = new Stack<(string Source, string Translated, bool ShowingOriginal)>();
            _textUndo[lineIndex] = history;
        }

        history.Push((
            _sourceTexts[lineIndex],
            _translatedTexts[lineIndex],
            _showingOriginalText.Contains(lineIndex)));
    }

    private void HighlightSelectedCard()
    {
        for (int i = 0; i < _cardHitRegions.Count; i++)
        {
            _cardHitRegions[i].Card.BorderThickness = i == _selectedLineIndex
                ? new Thickness(2)
                : new Thickness(1);
        }

        foreach (KeyValuePair<int, List<ResizeHandle>> pair in _resizeHandles)
        {
            foreach (ResizeHandle handle in pair.Value)
            {
                handle.Visibility = pair.Key == _selectedLineIndex
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        UpdateResizeHandles();
    }

    private void WindowSwitchTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_sourceWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr foregroundWindow = OSInterop.GetForegroundWindow();
        if (Environment.TickCount64 - _shownTimestamp < 1000)
        {
            return;
        }

        if (foregroundWindow != IntPtr.Zero &&
            foregroundWindow != _sourceWindow &&
            !IsOwnedByOverlay(foregroundWindow) &&
            !BelongsToThisProcess(foregroundWindow) &&
            !IsShellWindow(foregroundWindow))
        {
            Logger.LogInfo($"Closing result overlay after source-window switch. Source=0x{_sourceWindow.ToInt64():X}, foreground=0x{foregroundWindow.ToInt64():X}.");
            Close();
        }
    }

    private static bool BelongsToThisProcess(IntPtr windowHandle)
    {
        return OSInterop.GetWindowThreadProcessId(windowHandle, out uint processId) != 0 &&
               processId == Environment.ProcessId;
    }

    private static bool IsShellWindow(IntPtr windowHandle)
    {
        if (OSInterop.GetWindowThreadProcessId(windowHandle, out uint processId) == 0)
        {
            return false;
        }

        try
        {
            return string.Equals(
                Process.GetProcessById((int)processId).ProcessName,
                "explorer",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool IsOwnedByOverlay(IntPtr windowHandle)
    {
        IntPtr current = windowHandle;
        for (int depth = 0; current != IntPtr.Zero && depth < 8; depth++)
        {
            if (current == _hwnd)
            {
                return true;
            }

            current = OSInterop.GetWindow(current, OSInterop.GwOwner);
        }

        return false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RestoreHiddenButton_Click(object sender, RoutedEventArgs e)
    {
        _hiddenLineIndices.Clear();
        foreach (var region in _cardHitRegions)
        {
            region.Card.Visibility = Visibility.Visible;
        }

        CardContextMenu.Visibility = Visibility.Collapsed;
        ContextMenuCanvas.IsHitTestVisible = false;
    }

    private void CaptureRegionOutlineButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureRegionOutline.Visibility = CaptureRegionOutline.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        CaptureRegionOutlineButton.Content = CaptureRegionOutline.Visibility == Visibility.Visible
            ? "Region outline"
            : "Show outline";
    }

    private void RenderTranslatedBoxes()
    {
        ResultCanvas.Children.Clear();
        ResultCanvas.Children.Add(FrozenBackgroundImage);
        Canvas.SetZIndex(FrozenBackgroundImage, -1);
        ResultCanvas.Children.Add(CaptureRegionOutline);

        _cardHitRegions.Clear();
        _resizeHandles.Clear();
        var (_, _, captureWidthDip, captureHeightDip) = OverlayLayoutHelper.PhysicalToDip(
            _capturedRegion,
            _overlayBounds,
            _dpiScaleX,
            _dpiScaleY);
        var (captureLeftDip, captureTopDip, _, _) = OverlayLayoutHelper.PhysicalToDip(
            _capturedRegion,
            _overlayBounds,
            _dpiScaleX,
            _dpiScaleY);
        double captureRightDip = captureLeftDip + captureWidthDip;
        double captureBottomDip = captureTopDip + captureHeightDip;
        double overlayLeftDip = 0;
        double overlayRightDip = _overlayBounds.Width / _dpiScaleX;
        IReadOnlyList<PhysicalRect> lineBoundsDip = _lines
            .Select(line =>
            {
                var (left, top, width, height) = OverlayLayoutHelper.PhysicalToDip(
                    line.BoundingBox,
                    _overlayBounds,
                    _dpiScaleX,
                    _dpiScaleY);
                return new PhysicalRect(left, top, width, height);
            })
            .ToList();
        List<PhysicalRect> layoutObstacles = lineBoundsDip.ToList();
        IReadOnlyList<int> sourceLineCounts = _lines.Select(item => item.SourceLineCount).ToList();

        for (int lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
        {
            TranslatedLine line = _lines[lineIndex];
            int cardLineIndex = lineIndex;
            TranslatedLine cardLine = line;
            PhysicalRect lineBounds = lineBoundsDip[lineIndex];
            double leftDip = lineBounds.Left;
            double topDip = lineBounds.Top;
            double widthDip = lineBounds.Width;
            double heightDip = lineBounds.Height;

            double sourceLineHeightDip = heightDip / Math.Max(1, line.SourceLineCount);
            double estimatedFontSize = OverlayLayoutHelper.CalculateEstimatedFontSize(sourceLineHeightDip);
            var (initialWidth, initialMinHeight) = OverlayLayoutHelper.CalculateInitialCardSize(
                widthDip,
                heightDip,
                captureWidthDip,
                captureHeightDip,
                line.SourceLineCount);
            OverlayLayoutHelper.AdaptiveCardLayoutInput adaptiveInput = new(
                line.TranslatedText,
                line.OriginalText,
                lineBounds,
                layoutObstacles,
                sourceLineCounts,
                lineIndex,
                line.SourceLineCount,
                initialWidth,
                initialMinHeight,
                estimatedFontSize,
                captureLeftDip,
                captureRightDip,
                overlayLeftDip,
                overlayRightDip);

            TextBlock textBlock = new()
            {
                Text = line.TranslatedText,
                Foreground = CreateForegroundBrush(line),
                FontSize = estimatedFontSize,
                TextWrapping = TextWrapping.WrapWholeWords,
                VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = OverlayLayoutHelper.IsStrongRtlText(line.TranslatedText)
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight,
                TextAlignment = OverlayLayoutHelper.IsStrongRtlText(line.TranslatedText)
                    ? TextAlignment.Right
                    : TextAlignment.Left,
            };

            double measuredDesiredWidth = 0;
            double measuredReducedWidth = 0;
            if (OverlayLayoutHelper.IsAdaptiveTitleCandidate(adaptiveInput))
            {
                textBlock.TextWrapping = TextWrapping.NoWrap;
                textBlock.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                measuredDesiredWidth = textBlock.DesiredSize.Width;

                textBlock.FontSize = OverlayLayoutHelper.CalculateAdaptiveTitleFontSize(adaptiveInput);
                textBlock.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                measuredReducedWidth = textBlock.DesiredSize.Width;
            }

            OverlayLayoutHelper.AdaptiveCardLayout adaptiveLayout =
                OverlayLayoutHelper.CalculateAdaptiveInitialCardLayout(
                    adaptiveInput,
                    measuredDesiredWidth,
                    measuredReducedWidth);
            textBlock.FontSize = adaptiveLayout.FontSize;
            textBlock.TextWrapping = adaptiveLayout.FitsSingleLine
                ? TextWrapping.NoWrap
                : TextWrapping.WrapWholeWords;
            double cardContentWidth = Math.Max(1, adaptiveLayout.Width - 14);
            textBlock.Measure(new Windows.Foundation.Size(cardContentWidth, double.PositiveInfinity));
            double desiredCardHeight = OverlayLayoutHelper.CalculateDesiredCardHeight(
                textBlock.DesiredSize.Height,
                adaptiveLayout.MinHeight);
            OverlayLayoutHelper.VerticalCardPlacement verticalPlacement =
                OverlayLayoutHelper.CalculateNonOverlappingVerticalPlacement(
                    lineBounds,
                    layoutObstacles,
                    lineIndex,
                    adaptiveLayout.Left,
                    adaptiveLayout.Width,
                    desiredCardHeight,
                    captureTopDip,
                    captureBottomDip);

            while (!verticalPlacement.FitsWithoutOverlap &&
                   textBlock.TextWrapping != TextWrapping.NoWrap &&
                   textBlock.FontSize > 9)
            {
                textBlock.FontSize = Math.Max(9, textBlock.FontSize - 1);
                textBlock.Measure(new Windows.Foundation.Size(cardContentWidth, double.PositiveInfinity));
                desiredCardHeight = OverlayLayoutHelper.CalculateDesiredCardHeight(
                    textBlock.DesiredSize.Height,
                    adaptiveLayout.MinHeight);
                verticalPlacement = OverlayLayoutHelper.CalculateNonOverlappingVerticalPlacement(
                    lineBounds,
                    layoutObstacles,
                    lineIndex,
                    adaptiveLayout.Left,
                    adaptiveLayout.Width,
                    desiredCardHeight,
                    captureTopDip,
                    captureBottomDip);
            }

            Border card = new()
            {
                Background = CreateBackgroundBrush(line),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"] ?? new SolidColorBrush(Windows.UI.Color.FromArgb(100, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Width = adaptiveLayout.Width,
                MinHeight = verticalPlacement.MinHeight,
            };
            card.PointerPressed += Card_PointerPressed;
            card.PointerMoved += Card_PointerMoved;
            card.PointerReleased += Card_PointerReleased;
            card.PointerCanceled += Card_PointerCanceled;
            card.RightTapped += (_, args) =>
            {
                args.Handled = true;
                ShowCardActionMenu(card, cardLineIndex, args.GetPosition(card));
            };
            card.Child = textBlock;

            Canvas.SetLeft(card, adaptiveLayout.IsAdapted ? adaptiveLayout.Left : leftDip);
            Canvas.SetTop(card, verticalPlacement.Top);
            layoutObstacles[lineIndex] = new PhysicalRect(
                adaptiveLayout.IsAdapted ? adaptiveLayout.Left : leftDip,
                verticalPlacement.Top,
                adaptiveLayout.Width,
                verticalPlacement.MinHeight);
            _initialAppearance[lineIndex] = (
                textBlock.Text,
                GetBrushColor(card.Background),
                GetBrushColor(textBlock.Foreground),
                textBlock.FontSize,
                adaptiveLayout.IsAdapted ? adaptiveLayout.Left : leftDip,
                verticalPlacement.Top,
                card.Width,
                card.MinHeight);
            _translatedTexts[lineIndex] = line.TranslatedText;
            _sourceTexts[lineIndex] = line.OriginalText;

            ResultCanvas.Children.Add(card);
            Canvas.SetZIndex(card, lineIndex);
            _cardHitRegions.Add((line.BoundingBox, card));
        }
    }

    private async void OriginalTextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuLine is null ||
            _contextMenuCard is null ||
            _contextMenuLineIndex < 0 ||
            _contextMenuCard.Child is not TextBlock textBlock)
        {
            return;
        }

        int lineIndex = _contextMenuLineIndex;
        bool wasShowingOriginal = _showingOriginalText.Contains(lineIndex);
        PushTextUndo(lineIndex);
        if (wasShowingOriginal)
        {
            _showingOriginalText.Remove(lineIndex);
            if (_retranslateLine is not null)
            {
                TranslationLine sourceLine = new(
                    _sourceTexts[lineIndex],
                    _contextMenuLine.BoundingBox,
                    _contextMenuLine.Confidence,
                    _contextMenuLine.PolygonVertices,
                    _contextMenuLine.SourceLineCount);
                string sourceLanguage = GetSelectedLanguage(CardSourceLanguageComboBox, _sourceLanguage);
                string targetLanguage = GetSelectedLanguage(CardTargetLanguageComboBox, _targetLanguage);
                TranslationResult result = await _retranslateLine(sourceLine, sourceLanguage, targetLanguage);
                if (result.Success && result.Lines.Count > 0)
                {
                    _translatedTexts[lineIndex] = result.Lines[0].TranslatedText;
                }
                else
                {
                    _showingOriginalText.Add(lineIndex);
                    textBlock.Text = _sourceTexts[lineIndex];
                    SetOriginalTextButtonState(showOriginalText: false);
                    Logger.LogWarning($"Unable to retranslate edited source text for line {lineIndex}: {result.ErrorMessage}");
                    return;
                }
            }

            textBlock.Text = _translatedTexts[lineIndex];
            SetOriginalTextButtonState(showOriginalText: true);
        }
        else
        {
            _showingOriginalText.Add(lineIndex);
            textBlock.Text = _sourceTexts[lineIndex];
            SetOriginalTextButtonState(showOriginalText: false);
        }

        SetOriginalAllTextButtonState(_showingOriginalText.Count != _lines.Count);
        textBlock.InvalidateMeasure();
        _contextMenuCard.InvalidateMeasure();
    }

    private void ShowCardContextMenu(Border card, int lineIndex, TranslatedLine line)
    {
        CardActionMenu.Hide();
        _contextMenuLineIndex = lineIndex;
        _contextMenuLine = line;
        _selectedLineIndex = lineIndex;
        HighlightSelectedCard();
        _contextMenuCard = card;

        SelectLanguage(CardSourceLanguageComboBox, _sourceLanguage);
        SelectLanguage(CardTargetLanguageComboBox, _targetLanguage);
        SetOriginalTextButtonState(!_showingOriginalText.Contains(lineIndex));
        SetOriginalAllTextButtonState(_showingOriginalText.Count != _lines.Count);

        CollapseCardDetailPanels();
        CardContextMenu.Visibility = Visibility.Visible;
        ContextMenuCanvas.IsHitTestVisible = true;
        PositionContextMenu(card);
        DispatcherQueue.TryEnqueue(() => PositionContextMenu(card));
        FloatingToolbar.Visibility = Visibility.Visible;
        if (!_toolbarWasManuallyPositioned)
        {
            PositionToolbar(card);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_toolbarWasManuallyPositioned)
                {
                    PositionToolbar(card);
                }
            });
        }

        _isInitializingColorPickers = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (card.Background is SolidColorBrush backgroundBrush)
                {
                    BackgroundColorPicker.Color = backgroundBrush.Color;
                }

                if (card.Child is TextBlock textBlock &&
                    textBlock.Foreground is SolidColorBrush foregroundBrush)
                {
                    TextColorPicker.Color = foregroundBrush.Color;
                }

                UpdateSelectedFontSizeLabel();
            }
            finally
            {
                _isInitializingColorPickers = false;
            }
        });
    }

    private void ShowCardActionMenu(Border card, int lineIndex, Windows.Foundation.Point position)
    {
        CloseContextMenu();
        _selectedLineIndex = lineIndex;
        HighlightSelectedCard();
        _actionMenuLineIndex = lineIndex;
        _actionMenuCard = card;

        string displayedText = GetCardText(card);
        bool hasWebUri = CardActionHelper.TryGetWebUri(displayedText, out _actionMenuWebUri);
        bool hasEmailAddress = CardActionHelper.TryGetEmailAddress(displayedText, out _actionMenuEmailAddress);
        OpenLinkActionItem.Visibility = hasWebUri ? Visibility.Visible : Visibility.Collapsed;
        EmailActionItem.Visibility = hasEmailAddress ? Visibility.Visible : Visibility.Collapsed;
        DetectedContentSeparator.Visibility = hasWebUri || hasEmailAddress ? Visibility.Visible : Visibility.Collapsed;

        bool hasDistinctOriginalText =
            _sourceTexts.TryGetValue(lineIndex, out string? originalText) &&
            _translatedTexts.TryGetValue(lineIndex, out string? translatedText) &&
            !string.Equals(originalText, translatedText, StringComparison.Ordinal);
        SearchOriginalTextActionItem.Visibility = hasDistinctOriginalText ? Visibility.Visible : Visibility.Collapsed;

        FlyoutShowOptions options = new()
        {
            Position = position,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        };
        CardActionMenu.ShowAt(card, options);
    }

    private async void InitializeExternalActionAvailability()
    {
        bool copilotAvailable = await IsUriHandlerAvailableAsync(new Uri("ms-copilot://"), "Copilot");
        bool clickToDoAvailable = await IsUriHandlerAvailableAsync(new Uri("ms-clicktodo://"), "Click to Do");
        AskCopilotActionItem.Visibility = copilotAvailable ? Visibility.Visible : Visibility.Collapsed;
        OpenClickToDoActionItem.Visibility = clickToDoAvailable ? Visibility.Visible : Visibility.Collapsed;
        ExternalActionsSeparator.Visibility = copilotAvailable || clickToDoAvailable
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static async Task<bool> IsUriHandlerAvailableAsync(Uri uri, string actionName)
    {
        try
        {
            Windows.System.LaunchQuerySupportStatus status = await Windows.System.Launcher.QueryUriSupportAsync(
                uri,
                Windows.System.LaunchQuerySupportType.Uri);
            return status == Windows.System.LaunchQuerySupportStatus.Available;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Unable to query {actionName} availability: {ex.Message}");
            return false;
        }
    }

    private void CopyDisplayedTextActionItem_Click(object sender, RoutedEventArgs e)
    {
        CopyTextToClipboard(GetActionMenuText());
    }

    private async void OpenLinkActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMenuWebUri is not null)
        {
            await LaunchUriAsync(_actionMenuWebUri, "detected link");
        }
    }

    private async void EmailActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_actionMenuEmailAddress))
        {
            await LaunchUriAsync(new Uri($"mailto:{_actionMenuEmailAddress}"), "email composer");
        }
    }

    private async void SearchTranslatedTextActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (_translatedTexts.TryGetValue(_actionMenuLineIndex, out string? text))
        {
            await LaunchUriAsync(CardActionHelper.CreateSearchUri(text), "web search");
        }
    }

    private async void SearchOriginalTextActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceTexts.TryGetValue(_actionMenuLineIndex, out string? text))
        {
            await LaunchUriAsync(CardActionHelper.CreateSearchUri(text), "web search");
        }
    }

    private async void TranslateOriginalOnBingActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceTexts.TryGetValue(_actionMenuLineIndex, out string? text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            await LaunchUriAsync(
                CardActionHelper.CreateBingTranslatorUri(text, _targetLanguage),
                "Bing Translator");
        }
    }

    private void ShareDisplayedTextActionItem_Click(object sender, RoutedEventArgs e)
    {
        string text = GetActionMenuText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            int exStyle = OSInterop.GetWindowLong(_hwnd, OSInterop.GwlExStyle);
            _ = OSInterop.SetWindowLong(_hwnd, OSInterop.GwlExStyle, exStyle & ~OSInterop.WsExNoActivate);
            _ = OSInterop.SetForegroundWindow(_hwnd);
            _textShareService.Show(
                _hwnd,
                text,
                ResourceLoaderInstance.ResourceLoader.GetString("ShareTextTitle"));
        }
        catch (Exception ex)
        {
            Logger.LogError("Unable to open the Windows share dialog.", ex);
        }
    }

    private async void OpenClickToDoActionItem_Click(object sender, RoutedEventArgs e)
    {
        await LaunchUriAsync(new Uri("ms-clicktodo://"), "Click to Do");
    }

    private async void AskCopilotActionItem_Click(object sender, RoutedEventArgs e)
    {
        string text = GetActionMenuText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        CopyTextToClipboard(text);
        await LaunchCopilotWithClipboardTextAsync();
    }

    private static async Task LaunchCopilotWithClipboardTextAsync()
    {
        try
        {
            bool launched = await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-copilot:chat"));
            if (!launched)
            {
                Logger.LogWarning("Unable to launch Copilot.");
                return;
            }

            IntPtr copilotWindow = IntPtr.Zero;
            for (int attempt = 0; attempt < 20 && copilotWindow == IntPtr.Zero; attempt++)
            {
                await Task.Delay(250);
                foreach (Process process in Process.GetProcessesByName("copilotapp"))
                {
                    using (process)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            copilotWindow = process.MainWindowHandle;
                            break;
                        }
                    }
                }
            }

            if (copilotWindow == IntPtr.Zero || !OSInterop.SetForegroundWindow(copilotWindow))
            {
                Logger.LogWarning("Copilot opened, but its chat window could not be focused. The selected text remains on the clipboard.");
                return;
            }

            await Task.Delay(300);
            SendKeyChord(OSInterop.VK_CONTROL, OSInterop.VK_A);
            SendKeyChord(OSInterop.VK_CONTROL, OSInterop.VK_V);
        }
        catch (Exception ex)
        {
            Logger.LogError("Unable to send the selected text to Copilot. The selected text remains on the clipboard.", ex);
        }
    }

    private static void SendKeyChord(byte modifier, byte key)
    {
        OSInterop.KeybdEvent(modifier, 0, 0, UIntPtr.Zero);
        OSInterop.KeybdEvent(key, 0, 0, UIntPtr.Zero);
        OSInterop.KeybdEvent(key, 0, OSInterop.KEYEVENTF_KEYUP, UIntPtr.Zero);
        OSInterop.KeybdEvent(modifier, 0, OSInterop.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private async Task LaunchUriAsync(Uri uri, string actionName)
    {
        try
        {
            bool launched = await Windows.System.Launcher.LaunchUriAsync(uri);
            if (!launched)
            {
                Logger.LogWarning($"Unable to launch {actionName}.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Unable to launch {actionName}.", ex);
        }
    }

    private string GetActionMenuText()
    {
        return _actionMenuCard is null ? string.Empty : GetCardText(_actionMenuCard);
    }

    private static string GetCardText(Border card)
    {
        return card.Child switch
        {
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            _ => string.Empty,
        };
    }

    private void PositionContextMenu(Border card)
    {
        double overlayWidth = _overlayBounds.Width / _dpiScaleX;
        double overlayHeight = _overlayBounds.Height / _dpiScaleY;
        CardContextMenu.Measure(new Windows.Foundation.Size(Math.Max(320, overlayWidth - 16), double.PositiveInfinity));

        double menuWidth = Math.Max(320, CardContextMenu.DesiredSize.Width);
        double menuHeight = Math.Max(48, CardContextMenu.DesiredSize.Height);
        double cardLeft = Canvas.GetLeft(card);
        double cardTop = Canvas.GetTop(card);
        double cardHeight = Math.Max(card.ActualHeight, card.MinHeight);
        double left = Math.Clamp(cardLeft, 8, Math.Max(8, overlayWidth - menuWidth - 8));
        _isContextMenuAboveCard = cardTop >= menuHeight + 8;
        double top = _isContextMenuAboveCard
            ? cardTop - menuHeight - 4
            : cardTop + cardHeight + 4;

        Canvas.SetLeft(CardContextMenu, left);
        Canvas.SetTop(CardContextMenu, Math.Clamp(top, 8, Math.Max(8, overlayHeight - menuHeight - 8)));
        PositionCardDetailMenu(card);
    }

    private void PositionCardDetailMenu(Border card)
    {
        if (CardDetailMenu.Visibility != Visibility.Visible)
        {
            return;
        }

        double overlayWidth = _overlayBounds.Width / _dpiScaleX;
        double overlayHeight = _overlayBounds.Height / _dpiScaleY;
        CardDetailMenu.Measure(new Windows.Foundation.Size(Math.Max(320, overlayWidth - 16), double.PositiveInfinity));

        double detailWidth = Math.Max(48, CardDetailMenu.DesiredSize.Width);
        double detailHeight = Math.Max(48, CardDetailMenu.DesiredSize.Height);
        double primaryLeft = Canvas.GetLeft(CardContextMenu);
        double primaryTop = Canvas.GetTop(CardContextMenu);
        double primaryHeight = Math.Max(48, CardContextMenu.ActualHeight);
        double cardTop = Canvas.GetTop(card);
        double cardHeight = Math.Max(card.ActualHeight, card.MinHeight);
        double left = Math.Clamp(primaryLeft, 8, Math.Max(8, overlayWidth - detailWidth - 8));
        double top = OverlayLayoutHelper.CalculateDetailMenuTop(
            overlayHeight,
            detailHeight,
            primaryTop,
            primaryHeight,
            cardTop,
            cardHeight,
            _isContextMenuAboveCard);

        Canvas.SetLeft(CardDetailMenu, left);
        Canvas.SetTop(CardDetailMenu, top);
    }

    private void FormatPanelButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCardDetailPanel(FormatDetailPanel, FormatPanelButton);
    }

    private void TranslatePanelButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCardDetailPanel(TranslateDetailPanel, TranslatePanelButton);
    }

    private void ArrangePanelButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCardDetailPanel(ArrangeDetailPanel, ArrangePanelButton);
    }

    private void ToggleCardDetailPanel(FrameworkElement panel, ToggleButton selectedButton)
    {
        bool showPanel = selectedButton.IsChecked == true;
        CollapseCardDetailPanels();
        if (showPanel)
        {
            selectedButton.IsChecked = true;
            panel.Visibility = Visibility.Visible;
            CardDetailMenu.Visibility = Visibility.Visible;
        }

        if (_contextMenuCard is not null)
        {
            DispatcherQueue.TryEnqueue(() => PositionCardDetailMenu(_contextMenuCard));
        }
    }

    private void CollapseCardDetailPanels()
    {
        FormatDetailPanel.Visibility = Visibility.Collapsed;
        TranslateDetailPanel.Visibility = Visibility.Collapsed;
        ArrangeDetailPanel.Visibility = Visibility.Collapsed;
        CardDetailMenu.Visibility = Visibility.Collapsed;
        FormatPanelButton.IsChecked = false;
        TranslatePanelButton.IsChecked = false;
        ArrangePanelButton.IsChecked = false;
    }

    private void HideCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuLineIndex >= 0 && _contextMenuLineIndex < _cardHitRegions.Count)
        {
            _hiddenLineIndices.Add(_contextMenuLineIndex);
            _cardHitRegions[_contextMenuLineIndex].Card.Visibility = Visibility.Collapsed;
        }

        _selectedLineIndex = -1;
        HighlightSelectedCard();
        CardActionMenu.Hide();
        CloseContextMenu();
        _isInitializingColorPickers = false;
    }

    private void MoveCardUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedCardLayer(1);
    }

    private void MoveCardDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedCardLayer(-1);
    }

    private void BringCardToTopButton_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedCardLayer(toTop: true);
    }

    private void SendCardToBottomButton_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedCardLayer(toTop: false);
    }

    private void MoveSelectedCardLayer(int direction)
    {
        if (!TryGetSelectedCard(out Border? card))
        {
            return;
        }

        int cardZIndex = Canvas.GetZIndex(card);
        Border? adjacent = _cardHitRegions
            .Select(region => region.Card)
            .Where(candidate => candidate != card)
            .Where(candidate => direction > 0
                ? Canvas.GetZIndex(candidate) > cardZIndex
                : Canvas.GetZIndex(candidate) < cardZIndex)
            .OrderBy(candidate => Canvas.GetZIndex(candidate) * (direction > 0 ? 1 : -1))
            .FirstOrDefault();

        if (adjacent is null)
        {
            return;
        }

        int adjacentZIndex = Canvas.GetZIndex(adjacent);
        Canvas.SetZIndex(card, adjacentZIndex);
        Canvas.SetZIndex(adjacent, cardZIndex);
        EnsureResizeHandlesOnTop();
    }

    private void SetSelectedCardLayer(bool toTop)
    {
        if (!TryGetSelectedCard(out Border? card))
        {
            return;
        }

        int targetZIndex = toTop
            ? _cardHitRegions.Max(region => Canvas.GetZIndex(region.Card)) + 1
            : _cardHitRegions.Min(region => Canvas.GetZIndex(region.Card)) - 1;
        Canvas.SetZIndex(card, targetZIndex);
        EnsureResizeHandlesOnTop();
    }

    private bool TryGetSelectedCard(out Border? card)
    {
        card = _selectedLineIndex >= 0 && _selectedLineIndex < _cardHitRegions.Count
            ? _cardHitRegions[_selectedLineIndex].Card
            : null;
        return card is not null;
    }

    private void EnsureResizeHandlesOnTop()
    {
        foreach (List<ResizeHandle> handles in _resizeHandles.Values)
        {
            foreach (ResizeHandle handle in handles)
            {
                Canvas.SetZIndex(handle, 1000);
            }
        }
    }

    private void OriginalAllTextButton_Click(object sender, RoutedEventArgs e)
    {
        bool showOriginalText = _showingOriginalText.Count != _lines.Count;
        for (int lineIndex = 0; lineIndex < _cardHitRegions.Count; lineIndex++)
        {
            if (_cardHitRegions[lineIndex].Card.Child is not TextBlock textBlock)
            {
                continue;
            }

            if (showOriginalText)
            {
                _showingOriginalText.Add(lineIndex);
                textBlock.Text = _sourceTexts[lineIndex];
            }
            else
            {
                _showingOriginalText.Remove(lineIndex);
                textBlock.Text = _translatedTexts[lineIndex];
            }

            textBlock.InvalidateMeasure();
            _cardHitRegions[lineIndex].Card.InvalidateMeasure();
        }

        SetOriginalAllTextButtonState(!showOriginalText);
        if (_contextMenuLineIndex >= 0)
        {
            SetOriginalTextButtonState(!_showingOriginalText.Contains(_contextMenuLineIndex));
        }
    }

    private void EditCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveCard(out Border card) ||
            card.Child is not TextBlock textBlock)
        {
            return;
        }

        _editingOriginalText = textBlock.Text;
        _editingCard = card;
        ScreenTranslator.Helpers.WindowManager.SetResultCardEditing(true);
        _editingTextBox = new TextBox
        {
            Text = textBlock.Text,
            Foreground = textBlock.Foreground,
            FontSize = textBlock.FontSize,
            TextWrapping = TextWrapping.WrapWholeWords,
            AcceptsReturn = true,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        _editingTextBox.KeyDown += EditingTextBox_KeyDown;
        _editingTextBox.LostFocus += EditingTextBox_LostFocus;
        card.Child = _editingTextBox;
        int exStyle = OSInterop.GetWindowLong(_hwnd, OSInterop.GwlExStyle);
        _ = OSInterop.SetWindowLong(_hwnd, OSInterop.GwlExStyle, exStyle & ~OSInterop.WsExNoActivate);
        _ = OSInterop.SetForegroundWindow(_hwnd);
        _editingTextBox.Focus(FocusState.Programmatic);
        _editingTextBox.SelectAll();
        Logger.LogInfo($"Started editing overlay text for line {_contextMenuLineIndex}.");
    }

    private static void CopyTextToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        DataPackage package = new();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void EditingTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CancelEdit();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            CommitEdit();
            e.Handled = true;
        }
    }

    private void EditingTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitEdit();
    }

    private void CommitEdit()
    {
        if (_editingCard is null || _editingTextBox is null)
        {
            return;
        }

        TextBlock textBlock = new()
        {
            Text = _editingTextBox.Text,
            Foreground = _editingTextBox.Foreground,
            FontSize = _editingTextBox.FontSize,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _editingCard.Child = textBlock;
        if (_contextMenuLineIndex >= 0)
        {
            PushTextUndo(_contextMenuLineIndex);
            if (_showingOriginalText.Contains(_contextMenuLineIndex))
            {
                _sourceTexts[_contextMenuLineIndex] = textBlock.Text;
            }
            else
            {
                _translatedTexts[_contextMenuLineIndex] = textBlock.Text;
            }
        }

        Logger.LogInfo($"Committed edited overlay text for line {_contextMenuLineIndex}.");
        ClearEditState();
    }

    private void CancelEdit()
    {
        if (_editingCard is null || _editingTextBox is null)
        {
            return;
        }

        TextBlock textBlock = new()
        {
            Text = _editingOriginalText,
            Foreground = _editingTextBox.Foreground,
            FontSize = _editingTextBox.FontSize,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _editingCard.Child = textBlock;
        Logger.LogInfo($"Cancelled editing overlay text for line {_contextMenuLineIndex}.");
        ClearEditState();
    }

    private void ClearEditState()
    {
        if (_editingTextBox is not null)
        {
            _editingTextBox.KeyDown -= EditingTextBox_KeyDown;
            _editingTextBox.LostFocus -= EditingTextBox_LostFocus;
        }

        _editingTextBox = null;
        _editingCard = null;
        _editingOriginalText = string.Empty;
        ScreenTranslator.Helpers.WindowManager.SetResultCardEditing(false);

        int exStyle = OSInterop.GetWindowLong(_hwnd, OSInterop.GwlExStyle);
        _ = OSInterop.SetWindowLong(_hwnd, OSInterop.GwlExStyle, exStyle | OSInterop.WsExNoActivate);
    }

    private void BackgroundColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_isInitializingColorPickers && TryGetActiveCard(out Border card))
        {
            card.Background = new SolidColorBrush(args.NewColor);
            card.InvalidateMeasure();
            Logger.LogInfo($"Applied overlay background color to line {_contextMenuLineIndex}: alpha={args.NewColor.A}.");
        }
        else if (!_isInitializingColorPickers)
        {
            Logger.LogWarning("Ignored overlay background color change because no active card was selected.");
        }
    }

    private void TextColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_isInitializingColorPickers && TryGetActiveCard(out Border card))
        {
            if (card.Child is TextBlock textBlock)
            {
                textBlock.Foreground = new SolidColorBrush(args.NewColor);
                textBlock.InvalidateMeasure();
                Logger.LogInfo($"Applied overlay text color to line {_contextMenuLineIndex}.");
            }
            else if (card.Child is TextBox textBox)
            {
                textBox.Foreground = new SolidColorBrush(args.NewColor);
                textBox.InvalidateMeasure();
            }
        }
        else if (!_isInitializingColorPickers)
        {
            Logger.LogWarning("Ignored overlay text color change because no active card was selected.");
        }
    }

    private void DecreaseFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveCard(out Border card) && TryGetTextSizeElement(card, out TextBlock? textBlock, out TextBox? textBox))
        {
            double fontSize = textBlock?.FontSize ?? textBox!.FontSize;
            fontSize = Math.Max(9, fontSize - 1);
            if (textBlock is not null)
            {
                textBlock.FontSize = fontSize;
                textBlock.InvalidateMeasure();
            }
            else
            {
                textBox!.FontSize = fontSize;
                textBox.InvalidateMeasure();
            }

            card.InvalidateMeasure();
            UpdateSelectedFontSizeLabel();
            Logger.LogInfo($"Decreased overlay font size for line {_contextMenuLineIndex} to {fontSize}.");
        }
        else
        {
            Logger.LogWarning("Ignored decrease font size because no active card was selected.");
        }
    }

    private void IncreaseFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveCard(out Border card) && TryGetTextSizeElement(card, out TextBlock? textBlock, out TextBox? textBox))
        {
            double fontSize = textBlock?.FontSize ?? textBox!.FontSize;
            fontSize = Math.Min(48, fontSize + 1);
            if (textBlock is not null)
            {
                textBlock.FontSize = fontSize;
                textBlock.InvalidateMeasure();
            }
            else
            {
                textBox!.FontSize = fontSize;
                textBox.InvalidateMeasure();
            }

            card.InvalidateMeasure();
            UpdateSelectedFontSizeLabel();
            Logger.LogInfo($"Increased overlay font size for line {_contextMenuLineIndex} to {fontSize}.");
        }
        else
        {
            Logger.LogWarning("Ignored increase font size because no active card was selected.");
        }
    }

    private void RestoreInitialButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingCard is not null && ReferenceEquals(_editingCard, _contextMenuCard))
        {
            CancelEdit();
        }

        if (_contextMenuCard is not null &&
            _initialAppearance.TryGetValue(_contextMenuLineIndex, out var initial))
        {
            Border card = _contextMenuCard;
            card.Background = new SolidColorBrush(initial.Background);
            card.Width = initial.Width;
            card.MinHeight = initial.MinHeight;
            TextBlock textBlock = new()
            {
                Text = initial.Text,
                Foreground = new SolidColorBrush(initial.Foreground),
                FontSize = initial.FontSize,
                TextWrapping = TextWrapping.WrapWholeWords,
                VerticalAlignment = VerticalAlignment.Center,
            };
            card.Child = textBlock;
            Canvas.SetLeft(card, initial.Left);
            Canvas.SetTop(card, initial.Top);
            card.Visibility = Visibility.Visible;
            textBlock.InvalidateMeasure();
            card.InvalidateMeasure();
            _hiddenLineIndices.Remove(_contextMenuLineIndex);
            _showingOriginalText.Remove(_contextMenuLineIndex);
            _translatedTexts[_contextMenuLineIndex] = initial.Text;
            SetOriginalTextButtonState(showOriginalText: true);
            SetOriginalAllTextButtonState(_showingOriginalText.Count != _lines.Count);
            UpdateSelectedFontSizeLabel();
            PositionContextMenu(card);

            Logger.LogInfo($"Restored initial overlay text, appearance, and position for line {_contextMenuLineIndex}.");
        }
        else
        {
            Logger.LogWarning($"Could not restore initial overlay state for line {_contextMenuLineIndex}: no active card snapshot.");
        }
    }

    private async void ApplyOverallLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_retranslateAll is null)
        {
            return;
        }

        string sourceLanguage = GetSelectedLanguage(OverallSourceLanguageComboBox, "auto");
        string targetLanguage = GetSelectedLanguage(OverallTargetLanguageComboBox, "en-US");
        CloseContextMenu();
        ApplyOverallLanguageButton.IsEnabled = false;
        try
        {
            IReadOnlyList<TranslationLine> sourceLines = _lines
                .Select((line, index) => new TranslationLine(
                    _sourceTexts[index],
                    line.BoundingBox,
                    line.Confidence,
                    line.PolygonVertices,
                    line.SourceLineCount))
                .ToList();
            TranslationResult result = await _retranslateAll(sourceLines, sourceLanguage, targetLanguage);
            if (!result.Success || result.Lines.Count != _lines.Count)
            {
                Logger.LogWarning($"Failed to retranslate current capture: {result.ErrorMessage}");
                return;
            }

            _lines = result.Lines
                .Select((line, index) => line with
                {
                    OverlayBackgroundColorArgb = _lines[index].OverlayBackgroundColorArgb,
                    OverlayForegroundColorArgb = _lines[index].OverlayForegroundColorArgb,
                })
                .ToList();
            _sourceLanguage = sourceLanguage;
            _targetLanguage = targetLanguage;
            ResetCardStateForRetranslation();
            RenderTranslatedBoxes();
            PositionToolbar();
        }
        finally
        {
            ApplyOverallLanguageButton.IsEnabled = true;
        }
    }

    private void SwapCardLanguagesButton_Click(object sender, RoutedEventArgs e)
    {
        SwapLanguages(CardSourceLanguageComboBox, CardTargetLanguageComboBox);
    }

    private void SwapOverallLanguagesButton_Click(object sender, RoutedEventArgs e)
    {
        SwapLanguages(OverallSourceLanguageComboBox, OverallTargetLanguageComboBox);
    }

    private async void ApplyCardLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_retranslateLine is null ||
            _contextMenuLine is null ||
            !TryGetActiveCard(out Border card) ||
            _contextMenuLineIndex < 0)
        {
            return;
        }

        string sourceLanguage = GetSelectedLanguage(CardSourceLanguageComboBox, "auto");
        string targetLanguage = GetSelectedLanguage(CardTargetLanguageComboBox, "en-US");
        int cardLineIndex = _contextMenuLineIndex;
        TranslationLine sourceLine = new(
            _sourceTexts[cardLineIndex],
            _contextMenuLine.BoundingBox,
            _contextMenuLine.Confidence,
            _contextMenuLine.PolygonVertices,
            _contextMenuLine.SourceLineCount);

        CloseContextMenu();
        TranslationResult result = await _retranslateLine(sourceLine, sourceLanguage, targetLanguage);
        if (!result.Success || result.Lines.Count == 0)
        {
            Logger.LogWarning($"Failed to retranslate overlay line {cardLineIndex}: {result.ErrorMessage}");
            return;
        }

        if (card.Child is TextBlock textBlock)
        {
            PushTextUndo(cardLineIndex);
            _translatedTexts[cardLineIndex] = result.Lines[0].TranslatedText;
            if (!_showingOriginalText.Contains(cardLineIndex))
            {
                textBlock.Text = _translatedTexts[cardLineIndex];
            }

            textBlock.InvalidateMeasure();
            card.InvalidateMeasure();
        }
    }

    private static string GetSelectedLanguage(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag)
            ? tag
            : fallback;
    }

    private static void SwapLanguages(ComboBox sourceComboBox, ComboBox targetComboBox)
    {
        string sourceLanguage = GetSelectedLanguage(sourceComboBox, "auto");
        string targetLanguage = GetSelectedLanguage(targetComboBox, "en-US");
        var (newSourceLanguage, newTargetLanguage) =
            LanguageSelectionHelper.Swap(sourceLanguage, targetLanguage);
        SelectLanguage(sourceComboBox, newSourceLanguage);
        SelectLanguage(targetComboBox, newTargetLanguage);
    }

    private void ResetCardStateForRetranslation()
    {
        _initialAppearance.Clear();
        _translatedTexts.Clear();
        _sourceTexts.Clear();
        _textUndo.Clear();
        _showingOriginalText.Clear();
        _hiddenLineIndices.Clear();
        _selectedLineIndex = -1;
        _contextMenuLineIndex = -1;
        _contextMenuLine = null;
        _contextMenuCard = null;
        SetOriginalAllTextButtonState(showOriginalText: true);
    }

    private void SetOriginalTextButtonState(bool showOriginalText)
    {
        OriginalTextButton.Content = showOriginalText ? "Original" : "Translated";
    }

    private void UpdateSelectedFontSizeLabel()
    {
        if (TryGetActiveCard(out Border card) &&
            TryGetTextSizeElement(card, out TextBlock? textBlock, out TextBox? textBox))
        {
            double fontSize = textBlock?.FontSize ?? textBox!.FontSize;
            CurrentFontSizeText.Text = Math.Round(fontSize).ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    private void SetOriginalAllTextButtonState(bool showOriginalText)
    {
        OriginalAllTextButton.Content = showOriginalText ? "Original" : "Translated";
    }

    private static void SelectLanguage(ComboBox comboBox, string language)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                comboBoxItem.Tag is string itemLanguage &&
                LanguageSelectionHelper.AreEquivalent(itemLanguage, language))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }
    }

    private void CloseContextMenu()
    {
        CollapseCardDetailPanels();
        CardContextMenu.Visibility = Visibility.Collapsed;
        ContextMenuCanvas.IsHitTestVisible = false;
        _contextMenuCard = null;
        _contextMenuLine = null;
        _contextMenuLineIndex = -1;
    }

    private void ResultCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(ResultCanvas).Properties.IsLeftButtonPressed ||
            !ReferenceEquals(e.OriginalSource, ResultCanvas))
        {
            return;
        }

        _selectedLineIndex = -1;
        HighlightSelectedCard();
        CloseContextMenu();
        e.Handled = true;
    }

    private static Windows.UI.Color GetBrushColor(Brush brush)
    {
        return brush is SolidColorBrush solidBrush
            ? solidBrush.Color
            : Windows.UI.Color.FromArgb(255, 30, 30, 30);
    }

    private static bool TryGetTextSizeElement(Border card, out TextBlock? textBlock, out TextBox? textBox)
    {
        textBlock = card.Child as TextBlock;
        textBox = card.Child as TextBox;
        return textBlock is not null || textBox is not null;
    }

    private bool TryGetActiveCard(out Border card)
    {
        card = null!;
        if (_contextMenuCard is null || _contextMenuCard.Visibility != Visibility.Visible)
        {
            return false;
        }

        card = _contextMenuCard;
        return true;
    }

    private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border card || !e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(ResultCanvas);
        if (sender is Border selectedCard)
        {
            _selectedLineIndex = _cardHitRegions.FindIndex(region => ReferenceEquals(region.Card, selectedCard));
            HighlightSelectedCard();
            if (_selectedLineIndex >= 0 && _selectedLineIndex < _lines.Count)
            {
                ShowCardContextMenu(selectedCard, _selectedLineIndex, _lines[_selectedLineIndex]);
            }
        }

        _dragCard = card;
        _dragPointerId = point.PointerId;
        _dragStart = point.Position;
        _dragLeft = Canvas.GetLeft(card);
        _dragTop = Canvas.GetTop(card);
        card.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Card_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _dragCard) || e.Pointer.PointerId != _dragPointerId)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(ResultCanvas);
        Canvas.SetLeft(_dragCard, Math.Max(0, _dragLeft + point.Position.X - _dragStart.X));
        Canvas.SetTop(_dragCard, Math.Max(0, _dragTop + point.Position.Y - _dragStart.Y));
        UpdateResizeHandles();
        PositionContextMenu(_dragCard);

        e.Handled = true;
    }

    private void Card_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndCardDrag(e.Pointer);
    }

    private void Card_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndCardDrag(e.Pointer);
    }

    private void EndCardDrag(Pointer pointer)
    {
        if (_dragCard is not null)
        {
            _dragCard.ReleasePointerCapture(pointer);
        }

        _dragCard = null;
        _dragPointerId = 0;
    }

    private void ResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ResizeHandle handle ||
            !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed ||
            _selectedLineIndex < 0 ||
            _selectedLineIndex >= _cardHitRegions.Count)
        {
            return;
        }

        Border card = _cardHitRegions[_selectedLineIndex].Card;
        _activeResizeHandle = handle;
        _resizeStartWidth = card.ActualWidth;
        _resizeStartHeight = card.ActualHeight;
        _resizeStartLeft = Canvas.GetLeft(card);
        _resizeStartTop = Canvas.GetTop(card);
        _dragStart = e.GetCurrentPoint(ResultCanvas).Position;
        _dragPointerId = e.Pointer.PointerId;
        handle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _activeResizeHandle) ||
            e.Pointer.PointerId != _dragPointerId ||
            _selectedLineIndex < 0 ||
            _selectedLineIndex >= _cardHitRegions.Count)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(ResultCanvas);
        double deltaX = point.Position.X - _dragStart.X;
        double deltaY = point.Position.Y - _dragStart.Y;
        Border card = _cardHitRegions[_selectedLineIndex].Card;
        const double minWidth = 24;
        const double minHeight = 18;
        double left = _resizeStartLeft;
        double top = _resizeStartTop;
        double width = _resizeStartWidth;
        double height = _resizeStartHeight;

        switch (_activeResizeHandle.Direction)
        {
            case ResizeHandleDirection.North:
                top = Math.Min(_resizeStartTop + deltaY, _resizeStartTop + _resizeStartHeight - minHeight);
                height = _resizeStartHeight - (top - _resizeStartTop);
                break;
            case ResizeHandleDirection.NorthEast:
                top = Math.Min(_resizeStartTop + deltaY, _resizeStartTop + _resizeStartHeight - minHeight);
                height = _resizeStartHeight - (top - _resizeStartTop);
                width = Math.Max(minWidth, _resizeStartWidth + deltaX);
                break;
            case ResizeHandleDirection.East:
                width = Math.Max(minWidth, _resizeStartWidth + deltaX);
                break;
            case ResizeHandleDirection.SouthEast:
                width = Math.Max(minWidth, _resizeStartWidth + deltaX);
                height = Math.Max(minHeight, _resizeStartHeight + deltaY);
                break;
            case ResizeHandleDirection.South:
                height = Math.Max(minHeight, _resizeStartHeight + deltaY);
                break;
            case ResizeHandleDirection.SouthWest:
                left = Math.Min(_resizeStartLeft + deltaX, _resizeStartLeft + _resizeStartWidth - minWidth);
                width = _resizeStartWidth - (left - _resizeStartLeft);
                height = Math.Max(minHeight, _resizeStartHeight + deltaY);
                break;
            case ResizeHandleDirection.West:
                left = Math.Min(_resizeStartLeft + deltaX, _resizeStartLeft + _resizeStartWidth - minWidth);
                width = _resizeStartWidth - (left - _resizeStartLeft);
                break;
            case ResizeHandleDirection.NorthWest:
                left = Math.Min(_resizeStartLeft + deltaX, _resizeStartLeft + _resizeStartWidth - minWidth);
                top = Math.Min(_resizeStartTop + deltaY, _resizeStartTop + _resizeStartHeight - minHeight);
                width = _resizeStartWidth - (left - _resizeStartLeft);
                height = _resizeStartHeight - (top - _resizeStartTop);
                break;
        }

        Canvas.SetLeft(card, Math.Max(0, left));
        Canvas.SetTop(card, Math.Max(0, top));
        card.Width = width;
        card.Height = height;
        card.MinHeight = minHeight;
        UpdateResizeHandles();
        PositionContextMenu(card);
        e.Handled = true;
    }

    private void ResizeHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndResize(e.Pointer);
    }

    private void ResizeHandle_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndResize(e.Pointer);
    }

    private void EndResize(Pointer pointer)
    {
        _activeResizeHandle?.ReleasePointerCapture(pointer);
        _activeResizeHandle = null;
        _dragPointerId = 0;
    }

    private void UpdateResizeHandles()
    {
        if (_selectedLineIndex < 0 || _selectedLineIndex >= _cardHitRegions.Count)
        {
            return;
        }

        Border card = _cardHitRegions[_selectedLineIndex].Card;
        if (!_resizeHandles.TryGetValue(_selectedLineIndex, out List<ResizeHandle>? handles))
        {
            handles = new List<ResizeHandle>();
            foreach (ResizeHandleDirection direction in Enum.GetValues<ResizeHandleDirection>())
            {
                ResizeHandle handle = new(direction);
                handle.PointerPressed += ResizeHandle_PointerPressed;
                handle.PointerMoved += ResizeHandle_PointerMoved;
                handle.PointerReleased += ResizeHandle_PointerReleased;
                handle.PointerCanceled += ResizeHandle_PointerCanceled;
                handles.Add(handle);
                ResultCanvas.Children.Add(handle);
                Canvas.SetZIndex(handle, 1000);
            }

            _resizeHandles[_selectedLineIndex] = handles;
        }

        double left = Canvas.GetLeft(card);
        double top = Canvas.GetTop(card);
        double right = left + card.ActualWidth;
        double bottom = top + card.ActualHeight;
        foreach (ResizeHandle handle in handles)
        {
            double x = handle.Direction switch
            {
                ResizeHandleDirection.NorthWest or ResizeHandleDirection.West or ResizeHandleDirection.SouthWest => left - 4,
                ResizeHandleDirection.North or ResizeHandleDirection.South => ((left + right) / 2) - 4,
                _ => right - 4,
            };
            double y = handle.Direction switch
            {
                ResizeHandleDirection.NorthWest or ResizeHandleDirection.North or ResizeHandleDirection.NorthEast => top - 4,
                ResizeHandleDirection.West or ResizeHandleDirection.East => ((top + bottom) / 2) - 4,
                _ => bottom - 4,
            };
            Canvas.SetLeft(handle, x);
            Canvas.SetTop(handle, y);
            handle.Visibility = Visibility.Visible;
        }
    }

    private static Brush CreateBackgroundBrush(TranslatedLine line)
    {
        return line.OverlayBackgroundColorArgb.HasValue
            ? new SolidColorBrush(ToColor(line.OverlayBackgroundColorArgb.Value))
            : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] ??
              new SolidColorBrush(Windows.UI.Color.FromArgb(235, 30, 30, 30));
    }

    private static Brush CreateForegroundBrush(TranslatedLine line)
    {
        return line.OverlayForegroundColorArgb.HasValue
            ? new SolidColorBrush(ToColor(line.OverlayForegroundColorArgb.Value))
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] ??
              new SolidColorBrush(Colors.White);
    }

    private static Windows.UI.Color ToColor(uint argb)
    {
        return Windows.UI.Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }

    private void PositionToolbar(Border? selectedCard = null)
    {
        var (regionLeftDip, regionTopDip, _, _) = OverlayLayoutHelper.PhysicalToDip(
            _capturedRegion,
            _overlayBounds,
            _dpiScaleX,
            _dpiScaleY);

        double overlayWidth = _overlayBounds.Width / _dpiScaleX;
        double overlayHeight = _overlayBounds.Height / _dpiScaleY;
        FloatingToolbar.Measure(new Windows.Foundation.Size(Math.Max(320, overlayWidth - 16), double.PositiveInfinity));
        double toolbarWidth = Math.Max(48, FloatingToolbar.DesiredSize.Width);
        double toolbarHeight = Math.Max(48, FloatingToolbar.DesiredSize.Height);
        double toolbarLeft = Math.Clamp(regionLeftDip, 8, Math.Max(8, overlayWidth - toolbarWidth - 8));
        double toolbarTop = regionTopDip >= toolbarHeight + 16
            ? regionTopDip - toolbarHeight - 8
            : 8;

        if (selectedCard is not null)
        {
            double cardLeft = Canvas.GetLeft(selectedCard);
            double cardTop = Canvas.GetTop(selectedCard);
            double cardWidth = Math.Max(selectedCard.ActualWidth, selectedCard.Width);
            double cardBottom = cardTop + Math.Max(selectedCard.ActualHeight, selectedCard.MinHeight);
            bool overlapsSelectedCard =
                toolbarLeft < cardLeft + cardWidth &&
                toolbarLeft + toolbarWidth > cardLeft &&
                toolbarTop < cardBottom &&
                toolbarTop + toolbarHeight > cardTop;
            if (overlapsSelectedCard)
            {
                toolbarLeft = Math.Clamp(cardLeft, 8, Math.Max(8, overlayWidth - toolbarWidth - 8));
                toolbarTop = Math.Clamp(cardBottom + 8, 8, Math.Max(8, overlayHeight - toolbarHeight - 8));
            }
        }

        FloatingToolbar.Margin = new Thickness(toolbarLeft, toolbarTop, 0, 0);
    }

    private void OverallLanguagePanelButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleOverallDetailPanel(OverallLanguageDetailPanel, OverallLanguagePanelButton);
    }

    private void OverallViewPanelButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleOverallDetailPanel(OverallViewDetailPanel, OverallViewPanelButton);
    }

    private void ToggleOverallDetailPanel(FrameworkElement panel, ToggleButton selectedButton)
    {
        bool showPanel = selectedButton.IsChecked == true;
        CollapseOverallDetailPanels();
        if (showPanel)
        {
            selectedButton.IsChecked = true;
            panel.Visibility = Visibility.Visible;
            OverallDetailDivider.Visibility = Visibility.Visible;
        }

        DispatcherQueue.TryEnqueue(RepositionToolbarAfterLayout);
    }

    private void CollapseOverallDetailPanels()
    {
        OverallLanguageDetailPanel.Visibility = Visibility.Collapsed;
        OverallViewDetailPanel.Visibility = Visibility.Collapsed;
        OverallDetailDivider.Visibility = Visibility.Collapsed;
        OverallLanguagePanelButton.IsChecked = false;
        OverallViewPanelButton.IsChecked = false;
    }

    private void RepositionToolbarAfterLayout()
    {
        if (!_toolbarWasManuallyPositioned)
        {
            PositionToolbar(_contextMenuCard);
            return;
        }

        double overlayWidth = _overlayBounds.Width / _dpiScaleX;
        double overlayHeight = _overlayBounds.Height / _dpiScaleY;
        FloatingToolbar.Measure(new Windows.Foundation.Size(Math.Max(320, overlayWidth - 16), double.PositiveInfinity));
        double maxLeft = Math.Max(8, overlayWidth - Math.Max(48, FloatingToolbar.DesiredSize.Width) - 8);
        double maxTop = Math.Max(8, overlayHeight - Math.Max(48, FloatingToolbar.DesiredSize.Height) - 8);
        FloatingToolbar.Margin = new Thickness(
            Math.Clamp(FloatingToolbar.Margin.Left, 8, maxLeft),
            Math.Clamp(FloatingToolbar.Margin.Top, 8, maxTop),
            0,
            0);
    }

    private void FloatingToolbar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsInteractiveToolbarSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _isToolbarDragging = true;
        _toolbarPointerId = e.Pointer.PointerId;
        _toolbarDragStart = e.GetCurrentPoint(ResultCanvas).Position;
        _toolbarDragStartMargin = FloatingToolbar.Margin;
        FloatingToolbar.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void FloatingToolbar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isToolbarDragging || e.Pointer.PointerId != _toolbarPointerId)
        {
            return;
        }

        Windows.Foundation.Point current = e.GetCurrentPoint(ResultCanvas).Position;
        double maxLeft = Math.Max(8, (_overlayBounds.Width / _dpiScaleX) - Math.Max(48, FloatingToolbar.ActualWidth) - 8);
        double maxTop = Math.Max(8, (_overlayBounds.Height / _dpiScaleY) - Math.Max(48, FloatingToolbar.ActualHeight) - 8);
        double left = Math.Clamp(_toolbarDragStartMargin.Left + current.X - _toolbarDragStart.X, 8, maxLeft);
        double top = Math.Clamp(_toolbarDragStartMargin.Top + current.Y - _toolbarDragStart.Y, 8, maxTop);
        FloatingToolbar.Margin = new Thickness(left, top, 0, 0);
        _toolbarWasManuallyPositioned = true;
        e.Handled = true;
    }

    private void FloatingToolbar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isToolbarDragging || e.Pointer.PointerId != _toolbarPointerId)
        {
            return;
        }

        _isToolbarDragging = false;
        FloatingToolbar.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private bool IsInteractiveToolbarSource(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null && !ReferenceEquals(current, FloatingToolbar))
        {
            if (current is ButtonBase or ComboBox or ComboBoxItem)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void PositionCaptureRegionOutline()
    {
        var (leftDip, topDip, widthDip, heightDip) = OverlayLayoutHelper.PhysicalToDip(
            _capturedRegion,
            _overlayBounds,
            _dpiScaleX,
            _dpiScaleY);

        Canvas.SetLeft(CaptureRegionOutline, leftDip);
        Canvas.SetTop(CaptureRegionOutline, topDip);
        CaptureRegionOutline.Width = widthDip;
        CaptureRegionOutline.Height = heightDip;
        CaptureRegionOutline.Visibility = Visibility.Visible;
    }
}
