// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using AdaptiveCards.Rendering.WinUI3;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Microsoft.CmdPal.UI.Controls;

/// <summary>Creates Adaptive Card host configs that match Command Palette's theme, accent color, and contrast colors.</summary>
public sealed class AdaptiveCardsConfig
{
    // Accent and contrast changes create new configs, so keep only the latest few.
    private const int MaximumCachedConfigs = 4;

    private static readonly UISettings UserInterfaceSettings = new();
    private static readonly AccessibilitySettings AccessibilitySettings = new();
    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<string, AdaptiveHostConfig> CachedConfigs = new(StringComparer.Ordinal);

    public static AdaptiveHostConfig Light => Create(ElementTheme.Light);

    public static AdaptiveHostConfig Dark => Create(ElementTheme.Dark);

    /// <summary>
    /// Returns the host config for content shown in <paramref name="theme"/>, which must be
    /// <see cref="ElementTheme.Light"/> or <see cref="ElementTheme.Dark"/>.
    /// Configs are cached by resolved colors, but older entries may be evicted when the cache reaches its limit.
    /// </summary>
    public static AdaptiveHostConfig Create(ElementTheme theme)
    {
        if (theme is not ElementTheme.Light and not ElementTheme.Dark)
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        var json = AdaptiveHostConfigJson.Create(GetTokens(theme));
        lock (CacheLock)
        {
            if (CachedConfigs.TryGetValue(json, out var cached))
            {
                return cached;
            }

            if (CachedConfigs.Count >= MaximumCachedConfigs)
            {
                CachedConfigs.Clear();
            }

            var config = AdaptiveHostConfig.FromJsonString(json).HostConfig;
            CachedConfigs[json] = config;
            return config;
        }
    }

    private static AdaptiveCardThemeTokens GetTokens(ElementTheme theme)
    {
        if (AccessibilitySettings.HighContrast)
        {
            return AdaptiveCardThemeTokens.HighContrast(
                GetSystemColor(UIElementType.WindowText),
                GetSystemColor(UIElementType.GrayText),
                GetSystemColor(UIElementType.Highlight),
                GetSystemColor(UIElementType.Window));
        }

        // Match AccentTextFillColorPrimaryBrush: a darker accent on light surfaces and a
        // lighter accent on dark surfaces.
        return theme == ElementTheme.Light
            ? AdaptiveCardThemeTokens.Light(GetAccent(UIColorType.AccentDark2))
            : AdaptiveCardThemeTokens.Dark(GetAccent(UIColorType.AccentLight3));
    }

    private static string GetAccent(UIColorType type)
    {
        var color = UserInterfaceSettings.GetColorValue(type);
        return AdaptiveCardThemeTokens.ToHex(color.A, color.R, color.G, color.B);
    }

    /// <summary>
    /// Gets a contrast theme color with a typed call, since a color boxed in a resource dictionary
    /// can't be unboxed under native AOT.
    /// </summary>
    private static string GetSystemColor(UIElementType type)
    {
        var color = UserInterfaceSettings.UIElementColor(type);
        return AdaptiveCardThemeTokens.ToHex(color.A, color.R, color.G, color.B);
    }
}
