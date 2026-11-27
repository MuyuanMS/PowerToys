#pragma once
#include <Windows.h>
#include <functional>
#include "slow_hotkey_watchdog.h"

namespace CentralizedHotkeys
{
    struct Action
    {
        std::wstring moduleName;
        std::function<void(WORD, WORD)> action;

        Action(std::wstring moduleName = L"", std::function<void(WORD, WORD)> action = ([](WORD /*modifiersMask*/, WORD /*vkCode*/) {}))
        {
            this->moduleName = moduleName;
            this->action = action;
        }
    };

    struct Shortcut
    {
        WORD modifiersMask;
        WORD vkCode;
        int hotkeyID;

        Shortcut(WORD modifiersMask = 0, WORD vkCode = 0, const int hotkeyID = 0)
        {
            this->modifiersMask = modifiersMask;
            this->vkCode = vkCode;
            this->hotkeyID = hotkeyID;
        }

        bool operator<(const Shortcut& key) const
        {
            return std::pair<WORD, WORD>{ this->modifiersMask, this->vkCode } < std::pair<WORD, WORD>{ key.modifiersMask, key.vkCode };
        }
    };

    inline std::wstring ToWatchdogHotkey(const Shortcut& shortcut)
    {
        return SlowHotkeyWatchdog::DescribeHotkey(
            (shortcut.modifiersMask & MOD_WIN) != 0,
            (shortcut.modifiersMask & MOD_CONTROL) != 0,
            (shortcut.modifiersMask & MOD_SHIFT) != 0,
            (shortcut.modifiersMask & MOD_ALT) != 0,
            shortcut.vkCode);
    }

    std::wstring ToWstring(const Shortcut& shortcut);

    bool AddHotkeyAction(Shortcut shortcut, Action action);

    void UnregisterHotkeysForModule(std::wstring moduleName);

    void PopulateHotkey(Shortcut shortcut);

    void RegisterWindow(HWND hwnd);
}
