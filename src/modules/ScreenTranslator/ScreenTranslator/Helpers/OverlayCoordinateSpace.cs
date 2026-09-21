// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.UI.Xaml;
using ScreenTranslator.Core.Translation;

namespace ScreenTranslator.Helpers;

internal readonly record struct OverlayCoordinateSpace(
    PhysicalRect Bounds,
    double ScaleX,
    double ScaleY);

internal static class OverlayCoordinateSpaceHelper
{
    public static OverlayCoordinateSpace GetForClient(
        IntPtr windowHandle,
        FrameworkElement root,
        PhysicalRect fallbackBounds,
        double fallbackScaleX,
        double fallbackScaleY)
    {
        double rasterizationScale = root.XamlRoot?.RasterizationScale ?? 0;
        double scaleX = rasterizationScale > 0 ? rasterizationScale : Math.Max(1.0, fallbackScaleX);
        double scaleY = rasterizationScale > 0 ? rasterizationScale : Math.Max(1.0, fallbackScaleY);
        PhysicalRect bounds = fallbackBounds;

        OSInterop.POINT origin = default;
        if (OSInterop.GetClientRect(windowHandle, out OSInterop.RECT clientRect) &&
            OSInterop.ClientToScreen(windowHandle, ref origin) &&
            clientRect.Width > 0 &&
            clientRect.Height > 0)
        {
            bounds = new PhysicalRect(origin.X, origin.Y, clientRect.Width, clientRect.Height);
            if (root.ActualWidth > 0)
            {
                scaleX = clientRect.Width / root.ActualWidth;
            }

            if (root.ActualHeight > 0)
            {
                scaleY = clientRect.Height / root.ActualHeight;
            }
        }

        return new OverlayCoordinateSpace(bounds, scaleX, scaleY);
    }
}
