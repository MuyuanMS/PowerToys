#pragma once

#include "pch.h"

#include <common/utils/gpo.h>

class CopyAsUNCSettings
{
public:
    CopyAsUNCSettings();

    inline bool GetEnabled()
    {
        auto gpoSetting = powertoys_gpo::getConfiguredCopyAsUNCEnabledValue();
        if (gpoSetting == powertoys_gpo::gpo_rule_configured_enabled)
            return true;
        if (gpoSetting == powertoys_gpo::gpo_rule_configured_disabled)
            return false;
        RefreshEnabledState();
        return enabled;
    }

private:
    void RefreshEnabledState();

    bool enabled{ true };
    std::wstring generalJsonFilePath;
    FILETIME lastLoadedGeneralSettingsTime{};
};

CopyAsUNCSettings& CopyAsUNCSettingsInstance();
