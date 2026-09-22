// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Utilities;
using ScreenTranslator.Core.Layout;
using ScreenTranslator.Core.Ocr;
using ScreenTranslator.Core.Translation;
using ScreenTranslator.Keyboard;
using Windows.Graphics.Imaging;

namespace ScreenTranslator.Helpers;

public static class WindowManager
{
    private enum CaptureLaunchMode
    {
        Region,
        CurrentScreen,
        ActiveWindow,
        ScanText,
    }

    internal readonly record struct ResultOverlayKeyboardContext(
        bool IsVisible,
        bool IsEditingResultCard,
        bool HasSelectedResultCard,
        bool CanUndoSelectedResultCard,
        PhysicalRect? SelectedCardBounds,
        IntPtr OverlayWindowHandle,
        long LastInteractionTick);

    private static readonly List<SelectionOverlay> SelectionWindows = new();
    private static readonly List<BrushSelectionOverlay> BrushSelectionWindows = new();
    private static readonly List<ResultOverlay> ResultWindows = new();
    private static readonly Dictionary<ResultOverlay, ResultOverlayKeyboardContext> ResultOverlayKeyboardContexts = new();
    private static readonly object Lock = new();
    private static readonly TimeSpan ResultOverlayShortcutActiveDuration = TimeSpan.FromSeconds(10);
    private static ProcessingOverlay? _processingWindow;
    private static PhysicalRect? _foregroundWindowBounds;
    private static IntPtr _foregroundWindowHandle;
    private static volatile bool _resultCardEditing;
    private static ITranslationProvider? _translationProvider;
    private static string? _translationProviderSettingsKey;

    public static void LaunchScreenTranslatorOnEveryScreen()
    {
        LaunchScreenTranslator(CaptureLaunchMode.Region);
    }

    public static void TranslateCurrentScreen()
    {
        LaunchScreenTranslator(CaptureLaunchMode.CurrentScreen);
    }

    public static void TranslateActiveWindow()
    {
        LaunchScreenTranslator(CaptureLaunchMode.ActiveWindow);
    }

    public static void ScanText()
    {
        LaunchScreenTranslator(CaptureLaunchMode.ScanText);
    }

    private static void LaunchScreenTranslator(CaptureLaunchMode launchMode)
    {
        (IntPtr Handle, PhysicalRect Bounds)? activeWindowSnapshot = launchMode == CaptureLaunchMode.ActiveWindow
            ? ActiveWindowSnapshotReader.TryRead()
            : null;
        (IntPtr Handle, PhysicalRect? Bounds) foregroundWindow = activeWindowSnapshot.HasValue
            ? (activeWindowSnapshot.Value.Handle, activeWindowSnapshot.Value.Bounds)
            : CaptureForegroundWindow();
        if (activeWindowSnapshot.HasValue)
        {
            Logger.LogInfo(
                $"Using active window captured at hotkey time: HWND=0x{activeWindowSnapshot.Value.Handle.ToInt64():X}, " +
                $"bounds={activeWindowSnapshot.Value.Bounds.X},{activeWindowSnapshot.Value.Bounds.Y} " +
                $"{activeWindowSnapshot.Value.Bounds.Width}x{activeWindowSnapshot.Value.Bounds.Height}.");
        }

        CloseAllOverlays();

        Logger.LogInfo($"Launching ScreenTranslator with capture mode {launchMode}");

        ScreenTranslatorSettings? settings = null;
        try
        {
            settings = SettingsUtils.Default.GetSettingsOrDefault<ScreenTranslatorSettings>("ScreenTranslator");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to load ScreenTranslator settings: {ex.Message}");
        }

        string translationProviderSettingsKey = GetTranslationProviderSettingsKey(settings);
        if (_translationProvider is null ||
            !string.Equals(_translationProviderSettingsKey, translationProviderSettingsKey, StringComparison.Ordinal))
        {
            (_translationProvider as IDisposable)?.Dispose();
            _translationProvider = TranslationProviderFactory.Create(settings);
            _translationProviderSettingsKey = translationProviderSettingsKey;
        }

        IOcrBackend ocrBackend = CreateOcrBackend(settings);
        var provider = _translationProvider;
        var sourceLang = settings?.Properties?.SourceLanguage ?? "auto";
        var targetLang = settings?.Properties?.TargetLanguage ?? "en-US";
        var secondaryTargetLang = settings?.Properties?.SecondaryTargetLanguage ?? "zh-Hans";
        var freezeCapturedContent = settings?.Properties?.FreezeCapturedContent ?? false;
        (_foregroundWindowHandle, _foregroundWindowBounds) = foregroundWindow;

        IReadOnlyList<ScreenInfo> screens = MonitorHelper.GetAllScreens();
        if (launchMode == CaptureLaunchMode.ActiveWindow)
        {
            if (!_foregroundWindowBounds.HasValue)
            {
                Logger.LogWarning("Screen Translator could not determine the foreground application bounds.");
                return;
            }

            screens = [FindTargetScreen(_foregroundWindowBounds.Value)];
        }
        else if (launchMode is CaptureLaunchMode.CurrentScreen or CaptureLaunchMode.ScanText)
        {
            screens = _foregroundWindowBounds.HasValue
                ? [FindTargetScreen(_foregroundWindowBounds.Value)]
                : [screens.FirstOrDefault(screen => screen.IsPrimary) ?? screens[0]];
        }

        if (launchMode == CaptureLaunchMode.ScanText)
        {
            ScreenInfo screen = screens[0];
            SoftwareBitmap? capturedBitmap = Core.Capture.ScreenCaptureHelper.CaptureRegion(screen.Bounds);
            if (capturedBitmap == null)
            {
                Logger.LogWarning("Scan text screen capture returned null.");
                return;
            }

            BrushSelectionOverlay overlay = new(
                screen,
                capturedBitmap,
                ocrBackend,
                provider,
                sourceLang,
                targetLang,
                secondaryTargetLang);
            overlay.Closed += (s, e) =>
            {
                lock (Lock)
                {
                    BrushSelectionWindows.Remove(overlay);
                }
            };

            lock (Lock)
            {
                BrushSelectionWindows.Add(overlay);
            }

            overlay.Show();
            return;
        }

        lock (Lock)
        {
            foreach (var screen in screens)
            {
                bool showSelectionUi = launchMode == CaptureLaunchMode.Region;
                SelectionOverlay overlay = new(
                    screen,
                    provider,
                    sourceLang,
                    targetLang,
                    secondaryTargetLang,
                    _foregroundWindowBounds,
                    ocrBackend,
                    freezeCapturedContent,
                    showSelectionUi);
                overlay.Closed += (s, e) =>
                {
                    lock (Lock)
                    {
                        SelectionWindows.Remove(overlay);
                    }
                };

                SelectionWindows.Add(overlay);
                if (showSelectionUi)
                {
                    overlay.Show();
                }

                if (launchMode == CaptureLaunchMode.CurrentScreen)
                {
                    overlay.BeginFullScreenCapture();
                }
                else if (launchMode == CaptureLaunchMode.ActiveWindow)
                {
                    overlay.BeginActiveWindowCapture();
                }
            }
        }
    }

    private static IOcrBackend CreateOcrBackend(ScreenTranslatorSettings? settings)
    {
        string ocrProvider = settings?.Properties?.OcrProvider ?? "Automatic";
        if (string.Equals(ocrProvider, "AzureVision", StringComparison.OrdinalIgnoreCase))
        {
            return new AzureVisionOcrBackend(
                settings?.Properties?.AzureVisionEndpoint ?? string.Empty,
                ScreenTranslatorCredentialsVault.GetAzureVisionApiKey(),
                cloudConsentEnabled: true);
        }

        return new OcrBackendSelector().GetOrSelectBackendAsync().GetAwaiter().GetResult();
    }

    private static string GetTranslationProviderSettingsKey(ScreenTranslatorSettings? settings)
    {
        if (settings?.Properties is null)
        {
            return "Passthrough";
        }

        return string.Join(
            "\u001f",
            settings.Properties.SelectedProvider ?? "Passthrough",
            settings.Properties.AcpAgentCommand ?? string.Empty,
            settings.Properties.SourceLanguage ?? "auto",
            settings.Properties.TargetLanguage ?? "en-US",
            settings.Properties.SecondaryTargetLanguage ?? "zh-Hans",
            settings.Properties.AzureEndpoint ?? string.Empty,
            settings.Properties.AzureRegion ?? string.Empty,
            settings.Properties.LibreTranslateEndpoint ?? string.Empty);
    }

    private static (IntPtr Handle, PhysicalRect? Bounds) CaptureForegroundWindow()
    {
        IntPtr foregroundWindow = OSInterop.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero ||
            !OSInterop.IsWindowVisible(foregroundWindow) ||
            !OSInterop.GetWindowRect(foregroundWindow, out OSInterop.RECT windowRect) ||
            windowRect.Width <= 0 ||
            windowRect.Height <= 0)
        {
            return (IntPtr.Zero, null);
        }

        return (foregroundWindow, new PhysicalRect(windowRect.Left, windowRect.Top, windowRect.Width, windowRect.Height));
    }

    public static void CloseAllSelectionOverlays()
    {
        SelectionOverlay[] windowsToClose;
        lock (Lock)
        {
            windowsToClose = SelectionWindows.ToArray();
            SelectionWindows.Clear();
        }

        foreach (var window in windowsToClose)
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception closing SelectionOverlay: {ex.Message}");
            }
        }
    }

    public static void CloseOtherSelectionOverlays(SelectionOverlay retainedOverlay)
    {
        SelectionOverlay[] windowsToClose;
        lock (Lock)
        {
            windowsToClose = SelectionWindows.FindAll(window => !ReferenceEquals(window, retainedOverlay)).ToArray();
            SelectionWindows.RemoveAll(window => !ReferenceEquals(window, retainedOverlay));
        }

        foreach (SelectionOverlay window in windowsToClose)
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception closing SelectionOverlay: {ex.Message}");
            }
        }
    }

    public static void CloseAllResultOverlays()
    {
        ResultOverlay[] windowsToClose;
        lock (Lock)
        {
            windowsToClose = ResultWindows.ToArray();
            ResultWindows.Clear();
            foreach (ResultOverlay window in windowsToClose)
            {
                ResultOverlayKeyboardContexts.Remove(window);
            }
        }

        foreach (var window in windowsToClose)
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception closing ResultOverlay: {ex.Message}");
            }
        }
    }

    public static void CloseAllBrushSelectionOverlays()
    {
        BrushSelectionOverlay[] windowsToClose;
        lock (Lock)
        {
            windowsToClose = BrushSelectionWindows.ToArray();
            BrushSelectionWindows.Clear();
        }

        foreach (BrushSelectionOverlay window in windowsToClose)
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception closing BrushSelectionOverlay: {ex.Message}");
            }
        }
    }

    public static void CloseProcessingOverlay(ProcessingOverlay? overlay, bool cancelOperation = false)
    {
        if (overlay == null)
        {
            return;
        }

        lock (Lock)
        {
            if (!ReferenceEquals(_processingWindow, overlay))
            {
                return;
            }

            _processingWindow = null;
        }

        try
        {
            if (cancelOperation)
            {
                overlay.RequestCancellation();
            }

            overlay.Close();
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Exception closing ProcessingOverlay: {ex.Message}");
        }
    }

    public static ProcessingOverlay ShowProcessingOverlay(PhysicalRect capturedRegion, Action cancelOperation)
    {
        ProcessingOverlay? previous;
        lock (Lock)
        {
            previous = _processingWindow;
        }

        CloseProcessingOverlay(previous, cancelOperation: true);

        ScreenInfo targetScreen = FindTargetScreen(capturedRegion);
        ProcessingOverlay overlay = new(targetScreen, capturedRegion, cancelOperation);
        overlay.Closed += (s, e) =>
        {
            lock (Lock)
            {
                if (ReferenceEquals(_processingWindow, overlay))
                {
                    _processingWindow = null;
                }
            }
        };

        lock (Lock)
        {
            _processingWindow = overlay;
        }

        overlay.Show();
        return overlay;
    }

    public static void CloseAllOverlays()
    {
        CloseAllSelectionOverlays();
        CloseAllBrushSelectionOverlays();
        CloseAllResultOverlays();

        ProcessingOverlay? processingWindow;
        lock (Lock)
        {
            processingWindow = _processingWindow;
        }

        CloseProcessingOverlay(processingWindow, cancelOperation: true);
    }

    public static void UndoActiveResultCard()
    {
        ResultOverlay? overlay;
        lock (Lock)
        {
            overlay = ResultWindows.Count > 0 ? ResultWindows[^1] : null;
        }

        overlay?.UndoLastTextChange();
    }

    public static KeyboardShortcutContext GetKeyboardShortcutContext()
    {
        ResultOverlayKeyboardContext? context;
        lock (Lock)
        {
            ResultOverlay? overlay = ResultWindows.Count > 0 ? ResultWindows[^1] : null;
            context = overlay is not null && ResultOverlayKeyboardContexts.TryGetValue(overlay, out ResultOverlayKeyboardContext value)
                ? value
                : null;
        }

        if (!context.HasValue || !context.Value.IsVisible)
        {
            return default;
        }

        IntPtr foregroundWindow = OSInterop.GetForegroundWindow();
        bool cursorIsOverSelectedCard =
            context.Value.SelectedCardBounds.HasValue &&
            OSInterop.GetCursorPos(out OSInterop.POINT cursorPoint) &&
            Contains(context.Value.SelectedCardBounds.Value, cursorPoint);
        bool foregroundBelongsToResultOverlay =
            IsWindowInOwnerChain(foregroundWindow, context.Value.OverlayWindowHandle) ||
            (foregroundWindow != IntPtr.Zero &&
             BelongsToThisProcess(foregroundWindow) &&
             IsRecentlyInteractedWith(context.Value.LastInteractionTick));

        bool isActive = foregroundBelongsToResultOverlay || cursorIsOverSelectedCard;
        return new KeyboardShortcutContext(
            HasVisibleResultOverlay: context.Value.IsVisible,
            IsResultOverlayActive: isActive,
            IsEditingResultCard: context.Value.IsEditingResultCard,
            HasSelectedResultCard: context.Value.HasSelectedResultCard,
            CanUndoSelectedResultCard: context.Value.CanUndoSelectedResultCard);
    }

    internal static void UpdateResultOverlayKeyboardContext(ResultOverlay overlay, ResultOverlayKeyboardContext context)
    {
        lock (Lock)
        {
            if (ResultWindows.Contains(overlay))
            {
                ResultOverlayKeyboardContexts[overlay] = context;
            }
        }
    }

    internal static void RemoveResultOverlayKeyboardContext(ResultOverlay overlay)
    {
        lock (Lock)
        {
            ResultOverlayKeyboardContexts.Remove(overlay);
        }
    }

    private static bool IsRecentlyInteractedWith(long lastInteractionTick)
    {
        return lastInteractionTick > 0 &&
               Environment.TickCount64 - lastInteractionTick <= ResultOverlayShortcutActiveDuration.TotalMilliseconds;
    }

    private static bool IsWindowInOwnerChain(IntPtr windowHandle, IntPtr expectedOwner)
    {
        if (windowHandle == IntPtr.Zero || expectedOwner == IntPtr.Zero)
        {
            return false;
        }

        IntPtr current = windowHandle;
        for (int depth = 0; current != IntPtr.Zero && depth < 8; depth++)
        {
            if (current == expectedOwner)
            {
                return true;
            }

            current = OSInterop.GetWindow(current, OSInterop.GwOwner);
        }

        return false;
    }

    private static bool Contains(PhysicalRect bounds, OSInterop.POINT point)
    {
        return point.X >= bounds.Left &&
               point.X <= bounds.Right &&
               point.Y >= bounds.Top &&
               point.Y <= bounds.Bottom;
    }

    private static bool BelongsToThisProcess(IntPtr windowHandle)
    {
        return OSInterop.GetWindowThreadProcessId(windowHandle, out uint processId) != 0 &&
               processId == Environment.ProcessId;
    }

    public static bool IsEditingResultCard()
    {
        return _resultCardEditing;
    }

    public static void SetResultCardEditing(bool isEditing)
    {
        _resultCardEditing = isEditing;
    }

    public static void ShowResultOverlay(
        PhysicalRect capturedRegion,
        IReadOnlyList<TranslatedLine> lines,
        SoftwareBitmap capturedSnapshot,
        bool freezeCapturedContent,
        string sourceLanguage = "auto",
        string targetLanguage = "en-US",
        string secondaryTargetLanguage = "zh-Hans",
        Func<IReadOnlyList<TranslationLine>, string, string, string, Task<TranslationResult>>? retranslateAll = null,
        Func<TranslationLine, string, string, string, Task<TranslationResult>>? retranslateLine = null)
    {
        CloseAllResultOverlays();

        if (lines == null || lines.Count == 0)
        {
            capturedSnapshot.Dispose();
            Logger.LogInfo("No translated lines to display in ResultOverlay.");
            return;
        }

        ScreenInfo targetScreen = FindTargetScreen(capturedRegion);
        IReadOnlyList<TranslatedLine> sanitizedLines = OverlayLayoutHelper.SanitizeTranslatedLineGeometry(lines, capturedRegion);
        if (sanitizedLines.Count == 0)
        {
            capturedSnapshot.Dispose();
            Logger.LogInfo("No translated lines with valid geometry to display in ResultOverlay.");
            return;
        }

        Logger.LogInfo($"Displaying ResultOverlay on screen {targetScreen.Bounds.X},{targetScreen.Bounds.Y} with {sanitizedLines.Count} lines.");
        ResultOverlay overlay;
        try
        {
            overlay = new ResultOverlay(
                targetScreen,
                capturedRegion,
                sanitizedLines,
                capturedSnapshot,
                freezeCapturedContent,
                _foregroundWindowHandle,
                sourceLanguage,
                targetLanguage,
                secondaryTargetLanguage,
                retranslateAll,
                retranslateLine);
            Logger.LogInfo("ResultOverlay constructed successfully.");
        }
        catch (Exception ex)
        {
            capturedSnapshot.Dispose();
            Logger.LogError($"Failed to construct ResultOverlay: {ex}");
            return;
        }

        overlay.Closed += (s, e) =>
        {
            lock (Lock)
            {
                ResultWindows.Remove(overlay);
                ResultOverlayKeyboardContexts.Remove(overlay);
            }
        };

        lock (Lock)
        {
            ResultWindows.Add(overlay);
        }

        try
        {
            overlay.Show();
            Logger.LogInfo("ResultOverlay shown successfully.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to show ResultOverlay: {ex}");
            lock (Lock)
            {
                ResultWindows.Remove(overlay);
            }

            try
            {
                overlay.Close();
            }
            catch (Exception closeException)
            {
                Logger.LogWarning($"Failed to close ResultOverlay after show failure: {closeException.Message}");
            }
        }
    }

    private static ScreenInfo FindTargetScreen(PhysicalRect capturedRegion)
    {
        var screens = MonitorHelper.GetAllScreens();
        List<PhysicalRect> screenRects = new();
        foreach (var screen in screens)
        {
            screenRects.Add(screen.Bounds);
        }

        PhysicalRect? targetScreenRect = OverlayLayoutHelper.FindContainingScreen(capturedRegion, screenRects);
        ScreenInfo targetScreen = screens[0];

        if (targetScreenRect.HasValue)
        {
            foreach (var screen in screens)
            {
                if (screen.Bounds.X == targetScreenRect.Value.X && screen.Bounds.Y == targetScreenRect.Value.Y)
                {
                    targetScreen = screen;
                    break;
                }
            }
        }

        return targetScreen;
    }
}
