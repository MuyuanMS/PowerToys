#pragma once

#include <algorithm>
#include <initializer_list>
#include <unordered_set>

namespace MonitorRotation
{
    LONG ScaleCoordinate(LONG value, LONG sourceStart, LONG sourceSize, LONG targetStart, LONG targetSize) noexcept;
    RECT MapRectBetweenMonitorWorkAreas(const RECT& windowRect, const RECT& sourceWorkArea, const RECT& targetWorkArea) noexcept;
    size_t GetRotatedMonitorIndex(size_t sourceIndex, size_t monitorCount, bool reverse) noexcept;

    class KeyState
    {
    public:
        void Update(DWORD vkCode, bool isDown)
        {
            if (isDown)
            {
                m_pressedKeys.insert(vkCode);
            }
            else
            {
                m_pressedKeys.erase(vkCode);
            }
        }

        bool IsDown(DWORD vkCode) const noexcept
        {
            return m_pressedKeys.contains(vkCode);
        }

        bool IsAnyDown(std::initializer_list<DWORD> keys) const noexcept
        {
            return std::ranges::any_of(keys, [this](DWORD key) { return IsDown(key); });
        }

        void Consume(DWORD vkCode)
        {
            m_consumedKeys.insert(vkCode);
        }

        bool ReleaseWasConsumed(DWORD vkCode)
        {
            return m_consumedKeys.erase(vkCode) != 0;
        }

    private:
        std::unordered_set<DWORD> m_pressedKeys;
        std::unordered_set<DWORD> m_consumedKeys;
    };
}
