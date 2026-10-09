#pragma once

#include <common/logger/logger.h>

// Video Conference Mute was a utility we deprecated. However, this required a manual user disable of the module to remove the camera registration, so we include the disable code here to be able to clean up.
bool clean_video_conference()
{
    bool succeeded = true;
    const auto delete_tree = [&succeeded](HKEY root, const wchar_t* hive, const wchar_t* subkey) {
        const LSTATUS result = RegDeleteTreeW(root, subkey);
        if (result != ERROR_SUCCESS && result != ERROR_FILE_NOT_FOUND)
        {
            succeeded = false;
            Logger::warn(L"Failed to delete Video Conference Mute registry key {}\\{}; error: {}",
                         hive,
                         subkey,
                         result);
        }
    };

    // 31AD75E9-8C3A-49C8-B9ED-5880D6B4A764 is the CLSID GUID for the 64 video conference mute driver.
    // 31AD75E9-8C3A-49C8-B9ED-5880D6B4A732 is the CLSID GUID for the 32 video conference mute driver.
    // 860BB310-5D01-11D0-BD3B-00A0C911CE86 is the CLSID GUID for CLSID_VideoInputDeviceCategory.

    // Unregister the 64 bit driver CLSID:
    delete_tree(HKEY_CLASSES_ROOT, L"HKCR", L"CLSID\\{31AD75E9-8C3A-49C8-B9ED-5880D6B4A764}");
    // Unregister the 64 bit driver CLSID from Video Input Devices:
    delete_tree(HKEY_CLASSES_ROOT, L"HKCR", L"CLSID\\{860BB310-5D01-11D0-BD3B-00A0C911CE86}\\Instance\\{31AD75E9-8C3A-49C8-B9ED-5880D6B4A764}");
    // Unregister the 32 bit driver CLSID:
    delete_tree(HKEY_LOCAL_MACHINE, L"HKLM", L"Software\\WOW6432Node\\Classes\\CLSID\\{31AD75E9-8C3A-49C8-B9ED-5880D6B4A732}");
    // Unregister the 32 bit driver CLSID from Video Input Devices:
    delete_tree(HKEY_LOCAL_MACHINE, L"HKLM", L"Software\\WOW6432Node\\Classes\\CLSID\\{860BB310-5D01-11D0-BD3B-00A0C911CE86}\\Instance\\{31AD75E9-8C3A-49C8-B9ED-5880D6B4A732}");

    return succeeded;
}
