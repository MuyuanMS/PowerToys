#pragma once

namespace LightSwitchBrightnessLogic
{
    constexpr bool IsKnown(int brightness)
    {
        return brightness >= 0;
    }

    constexpr bool ShouldBeLight(int brightness, int threshold)
    {
        return brightness >= threshold;
    }

    constexpr bool CrossedThreshold(int previousBrightness, int brightness, int threshold)
    {
        return IsKnown(previousBrightness) &&
            (ShouldBeLight(previousBrightness, threshold) != ShouldBeLight(brightness, threshold));
    }
}
