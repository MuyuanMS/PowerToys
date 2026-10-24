#include "pch.h"
#include "Settings.h"
#include "Constants.h"

#include <common/utils/json.h>
#include <common/SettingsAPI/settings_helpers.h>

static bool LastModifiedTime(const std::wstring& filePath, FILETIME* lpFileTime)
{
    WIN32_FILE_ATTRIBUTE_DATA attr{};
    if (GetFileAttributesExW(filePath.c_str(), GetFileExInfoStandard, &attr))
    {
        *lpFileTime = attr.ftLastWriteTime;
        return true;
    }
    return false;
}

CopyAsUNCSettings::CopyAsUNCSettings()
{
    generalJsonFilePath = PTSettingsHelper::get_powertoys_general_save_file_location();
    RefreshEnabledState();
}

void CopyAsUNCSettings::RefreshEnabledState()
{
    FILETIME lastModifiedTime{};
    if (!(LastModifiedTime(generalJsonFilePath, &lastModifiedTime) &&
          CompareFileTime(&lastModifiedTime, &lastLoadedGeneralSettingsTime) == 1))
        return;

    lastLoadedGeneralSettingsTime = lastModifiedTime;

    auto json = json::from_file(generalJsonFilePath);
    if (!json)
        return;

    const json::JsonObject& jsonSettings = json.value();
    try
    {
        json::JsonObject modulesEnabledState;
        json::get(jsonSettings, L"enabled", modulesEnabledState, json::JsonObject{});
        json::get(modulesEnabledState, L"Copy as UNC", enabled, true);
    }
    catch (const winrt::hresult_error&)
    {
    }
}

CopyAsUNCSettings& CopyAsUNCSettingsInstance()
{
    static CopyAsUNCSettings instance;
    return instance;
}
