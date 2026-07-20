#pragma once

#include "ThemeScheduler.h"

#include <exception>
#include <optional>
#include <string>
#include <utility>

namespace LightSwitch
{
    template<typename SunTimeCalculator, typename SettingsSaver>
    std::optional<std::pair<int, int>> TryUpdateSunTimes(
        const std::wstring& latitudeText,
        const std::wstring& longitudeText,
        SunTimeCalculator calculateSunTimes,
        SettingsSaver saveSunTimes)
    {
        int newLightTime;
        int newDarkTime;

        try
        {
            const double latitude = std::stod(latitudeText);
            const double longitude = std::stod(longitudeText);

            SYSTEMTIME st;
            GetLocalTime(&st);

            const SunTimes newTimes = calculateSunTimes(latitude, longitude, st.wYear, st.wMonth, st.wDay);
            newLightTime = newTimes.sunriseHour * 60 + newTimes.sunriseMinute;
            newDarkTime = newTimes.sunsetHour * 60 + newTimes.sunsetMinute;
        }
        catch (const std::exception&)
        {
            return std::nullopt;
        }

        try
        {
            saveSunTimes(newLightTime, newDarkTime);
        }
        catch (...)
        {
        }

        return std::make_pair(newLightTime, newDarkTime);
    }
}
