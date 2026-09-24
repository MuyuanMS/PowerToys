#pragma once

#include <string>

#include <Windows.h>

namespace KeyboardManagerAutoSwitchPolicy
{
    struct State
    {
        std::wstring pendingTarget;
        int pendingCount = 0;
        std::wstring requestedProfile;
    };

    inline bool IsModifier(USHORT vkey)
    {
        switch (vkey)
        {
        case VK_SHIFT:
        case VK_CONTROL:
        case VK_MENU:
        case VK_LSHIFT:
        case VK_RSHIFT:
        case VK_LCONTROL:
        case VK_RCONTROL:
        case VK_LMENU:
        case VK_RMENU:
        case VK_LWIN:
        case VK_RWIN:
            return true;
        default:
            return false;
        }
    }

    inline bool ShouldIgnoreEvent(bool injected, bool keyDown, USHORT vkey)
    {
        return injected || !keyDown || IsModifier(vkey);
    }

    inline bool IsAwaitingRequestedProfile(const State& state, const std::wstring& target, const std::wstring& current)
    {
        return target != current && target == state.requestedProfile;
    }

    inline bool AdvanceHysteresis(State& state, const std::wstring& target, int threshold)
    {
        if (target == state.pendingTarget)
        {
            ++state.pendingCount;
        }
        else
        {
            state.pendingTarget = target;
            state.pendingCount = 1;
        }

        if (state.pendingCount < threshold)
        {
            return false;
        }

        state.pendingTarget.clear();
        state.pendingCount = 0;
        return true;
    }
}
