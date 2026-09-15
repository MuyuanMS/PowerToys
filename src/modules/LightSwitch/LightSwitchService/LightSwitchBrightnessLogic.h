#pragma once

namespace LightSwitchBrightnessLogic
{
    struct Transition
    {
        bool isKnown;
        bool clearsManualOverride;
        bool shouldApplyTheme;
        bool shouldBeLight;
    };

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

    constexpr Transition EvaluateTransition(int previousBrightness, int brightness, int threshold, bool isManualOverride)
    {
        if (!IsKnown(brightness))
        {
            return { false, false, false, false };
        }

        const bool clearsManualOverride =
            isManualOverride && CrossedThreshold(previousBrightness, brightness, threshold);
        return {
            true,
            clearsManualOverride,
            !isManualOverride || clearsManualOverride,
            ShouldBeLight(brightness, threshold),
        };
    }
}
