#include "pch.h"
#include "MouseButtonsHook.h"
#include <common/debug_control.h>

#pragma region public

HHOOK MouseButtonsHook::hHook = {};
std::function<void()> MouseButtonsHook::secondaryClickCallback = {};
std::function<void()> MouseButtonsHook::middleClickCallback = {};
std::function<bool(bool)> MouseButtonsHook::wheelCallback = {};
int MouseButtonsHook::wheelDeltaAccumulator = 0;

MouseButtonsHook::MouseButtonsHook(std::function<void()> extRightClickCallback, std::function<void()> extMiddleClickCallback, std::function<bool(bool)> extWheelCallback)
{
    secondaryClickCallback = std::move(extRightClickCallback);
    middleClickCallback = std::move(extMiddleClickCallback);
    wheelCallback = std::move(extWheelCallback);
}

void MouseButtonsHook::enable()
{
#if defined(DISABLE_LOWLEVEL_HOOKS_WHEN_DEBUGGED)
    if (IsDebuggerPresent())
    {
        return;
    }
#endif
    if (!hHook)
    {
        hHook = SetWindowsHookEx(WH_MOUSE_LL, MouseButtonsProc, GetModuleHandle(NULL), 0);
    }
}

void MouseButtonsHook::disable()
{
    wheelDeltaAccumulator = 0;

    if (hHook)
    {
        UnhookWindowsHookEx(hHook);
        hHook = NULL;
    }
}

#pragma endregion

#pragma region private

LRESULT CALLBACK MouseButtonsHook::MouseButtonsProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode == HC_ACTION)
    {
        if (wParam == WM_RBUTTONDOWN || wParam == WM_XBUTTONDOWN)
        {
            secondaryClickCallback();
        }
        else if (wParam == WM_MBUTTONDOWN)
        {
            middleClickCallback();
        }
        else if (wParam == WM_MOUSEWHEEL)
        {
            const auto delta = GET_WHEEL_DELTA_WPARAM(reinterpret_cast<MSLLHOOKSTRUCT*>(lParam)->mouseData);
            wheelDeltaAccumulator += delta;

            bool handled = false;
            while (std::abs(wheelDeltaAccumulator) >= WHEEL_DELTA)
            {
                const bool up = wheelDeltaAccumulator > 0;
                if (!wheelCallback(up))
                {
                    wheelDeltaAccumulator = 0;
                    break;
                }

                handled = true;
                wheelDeltaAccumulator += up ? -WHEEL_DELTA : WHEEL_DELTA;
            }

            if (handled)
            {
                return 1;
            }
        }
    }
    return CallNextHookEx(hHook, nCode, wParam, lParam);
}

#pragma endregion
