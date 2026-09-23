#include "pch.h"
#include "DraggingState.h"

#include <FancyZonesLib/Settings.h>
#include <FancyZonesLib/util.h>

DraggingState::DraggingState(const std::function<void()>& keyUpdateCallback, const std::function<bool(bool)>& canSwitchLayoutByWheelCallback, const std::function<bool(bool)>& layoutSwitchByWheelCallback) :
    m_secondaryMouseState(false),
    m_middleMouseState(false),
    m_mouseHook(std::bind(&DraggingState::OnSecondaryMouseDown, this), std::bind(&DraggingState::OnMiddleMouseDown, this), std::bind(&DraggingState::IsMouseWheelLayoutSwitchActive, this, std::placeholders::_1), std::bind(&DraggingState::OnMouseWheel, this, std::placeholders::_1)),
    m_ctrlKeyState(keyUpdateCallback),
    m_keyUpdateCallback(keyUpdateCallback),
    m_canSwitchLayoutByWheelCallback(canSwitchLayoutByWheelCallback),
    m_layoutSwitchByWheelCallback(layoutSwitchByWheelCallback)
{
}

void DraggingState::Enable()
{
    if (FancyZonesSettings::settings().mouseSwitch || FancyZonesSettings::settings().mouseWheelLayoutSwitch)
    {
        m_mouseHook.enable();
    }

    m_ctrlKeyState.enable();

    // m_shift is fed by raw input (FancyZones::OnKeyboardInput), which cannot
    // observe Shift transitions delivered while the secure desktop, a detached
    // session leg (RDP attach/detach), or an elevated foreground window owns
    // input, so the latched value can go stale between drags. Re-seed it from
    // the live key state at drag start, the same way KeyState::enable()
    // re-seeds Ctrl above. Raw input still drives mid-drag transitions.
    m_shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
}

void DraggingState::Disable()
{
    m_dragging = false;
    m_secondaryMouseState = false;
    m_middleMouseState = false;
    m_shift = false;

    m_mouseHook.disable();
    m_ctrlKeyState.disable();
}

void DraggingState::UpdateDraggingState() noexcept
{
    // This updates m_dragging depending on if the shift key is being held down
    if (FancyZonesSettings::settings().shiftDrag)
    {
        m_dragging = (m_shift ^ m_secondaryMouseState);
    }
    else
    {
        m_dragging = !(m_shift ^ m_secondaryMouseState);
    }
}

void DraggingState::OnSecondaryMouseDown()
{
    if (!FancyZonesSettings::settings().mouseSwitch)
    {
        return;
    }

    m_secondaryMouseState = !m_secondaryMouseState;
    m_keyUpdateCallback();
}

bool DraggingState::OnMouseWheel(bool up)
{
    if (IsMouseWheelLayoutSwitchActive(up))
    {
        return m_layoutSwitchByWheelCallback(up);
    }

    return false;
}

bool DraggingState::IsMouseWheelLayoutSwitchActive(bool up) const noexcept
{
    return m_dragging && FancyZonesSettings::settings().mouseWheelLayoutSwitch && m_canSwitchLayoutByWheelCallback(up);
}

void DraggingState::OnMiddleMouseDown()
{
    if (FancyZonesSettings::settings().mouseMiddleClickSpanningMultipleZones)
    {
        m_middleMouseState = !m_middleMouseState;
    }
    else if (FancyZonesSettings::settings().mouseSwitch)
    {
        m_secondaryMouseState = !m_secondaryMouseState;
    }
    else
    {
        return;
    }

    m_keyUpdateCallback();
}

bool DraggingState::IsDragging() const noexcept
{
    return m_dragging;
}

bool DraggingState::IsSelectManyZonesState() const noexcept
{
    return m_ctrlKeyState.state() || m_middleMouseState;
}

void DraggingState::SetShiftState(bool value) noexcept
{
    m_shift = value;
    m_keyUpdateCallback();
}
