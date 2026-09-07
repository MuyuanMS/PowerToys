// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma once

#include <array>
#include <string>
#include <vector>
#include <windows.h>

namespace auto_hide_cursor
{
    inline std::wstring RecoveryMarkerPath() noexcept
    {
        wchar_t localAppData[MAX_PATH]{};
        const auto length = GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, std::size(localAppData));
        if (length == 0 || length >= std::size(localAppData))
        {
            return {};
        }

        return std::wstring{ localAppData, length } + L"\\Microsoft\\PowerToys\\AutoHideCursor\\cursor-recovery.marker";
    }

    inline bool CreateRecoveryMarker() noexcept
    {
        const auto markerPath = RecoveryMarkerPath();
        if (markerPath.empty())
        {
            return false;
        }

        const auto moduleDirectory = markerPath.substr(0, markerPath.find_last_of(L'\\'));
        const auto powerToysDirectory = moduleDirectory.substr(0, moduleDirectory.find_last_of(L'\\'));
        CreateDirectoryW(powerToysDirectory.c_str(), nullptr);
        CreateDirectoryW(moduleDirectory.c_str(), nullptr);

        const auto marker = CreateFileW(
            markerPath.c_str(),
            GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_HIDDEN,
            nullptr);
        if (marker == INVALID_HANDLE_VALUE)
        {
            return false;
        }

        CloseHandle(marker);
        return true;
    }

    inline bool HasRecoveryMarker() noexcept
    {
        const auto markerPath = RecoveryMarkerPath();
        if (markerPath.empty())
        {
            return false;
        }

        const auto marker = CreateFileW(
            markerPath.c_str(),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_HIDDEN,
            nullptr);
        if (marker == INVALID_HANDLE_VALUE)
        {
            return false;
        }

        CloseHandle(marker);
        return true;
    }

    inline void RemoveRecoveryMarker() noexcept
    {
        const auto markerPath = RecoveryMarkerPath();
        if (!markerPath.empty())
        {
            DeleteFileW(markerPath.c_str());
        }
    }

    inline void RefreshCurrentCursor() noexcept
    {
        POINT cursorPosition{};
        if (!GetCursorPos(&cursorPosition))
        {
            return;
        }

        const auto window = WindowFromPoint(cursorPosition);
        if (!window)
        {
            return;
        }

        DWORD_PTR hitTest = HTCLIENT;
        SendMessageTimeoutW(
            window,
            WM_NCHITTEST,
            0,
            MAKELPARAM(cursorPosition.x, cursorPosition.y),
            SMTO_ABORTIFHUNG | SMTO_BLOCK,
            100,
            &hitTest);
        SendMessageTimeoutW(
            window,
            WM_SETCURSOR,
            reinterpret_cast<WPARAM>(window),
            MAKELPARAM(hitTest, WM_MOUSEMOVE),
            SMTO_ABORTIFHUNG | SMTO_BLOCK,
            100,
            nullptr);
    }

    inline bool RestoreSystemCursors() noexcept
    {
        if (!SystemParametersInfoW(SPI_SETCURSORS, 0, nullptr, 0))
        {
            return false;
        }

        RefreshCurrentCursor();
        RemoveRecoveryMarker();
        return true;
    }

    inline void RestoreSystemCursorsIfMarked() noexcept
    {
        if (HasRecoveryMarker())
        {
            RestoreSystemCursors();
        }
    }

    // SetSystemCursor is the only supported API that crosses application boundaries.
    // The worker/module supervision pair guarantees that the user's configured scheme is reloaded.
    class SystemCursorHider
    {
    public:
        ~SystemCursorHider()
        {
            Restore();
        }

        bool Hide() noexcept
        {
            if (m_hidden)
            {
                return true;
            }

            if (!CreateRecoveryMarker())
            {
                SetLastError(ERROR_CANNOT_MAKE);
                return false;
            }

            for (const auto cursorId : systemCursorIds)
            {
                const auto transparentCursor = CreateTransparentCursor();
                if (!transparentCursor)
                {
                    RestoreSystemCursors();
                    return false;
                }

                if (!SetSystemCursor(transparentCursor, cursorId))
                {
                    const auto error = GetLastError();
                    DestroyCursor(transparentCursor);
                    RestoreSystemCursors();
                    SetLastError(error);
                    return false;
                }
            }

            m_hidden = true;
            RefreshCurrentCursor();
            return true;
        }

        bool Restore() noexcept
        {
            if (!m_hidden)
            {
                return true;
            }

            if (!RestoreSystemCursors())
            {
                return false;
            }

            m_hidden = false;
            return true;
        }

    private:
        static HCURSOR CreateTransparentCursor() noexcept
        {
            const auto width = GetSystemMetrics(SM_CXCURSOR);
            const auto height = GetSystemMetrics(SM_CYCURSOR);
            if (width <= 0 || height <= 0)
            {
                return nullptr;
            }

            const auto bytesPerScanLine = ((static_cast<size_t>(width) + 15u) / 16u) * 2u;
            const auto maskSize = bytesPerScanLine * static_cast<size_t>(height);
            std::vector<BYTE> andMask(maskSize, 0xFF);
            std::vector<BYTE> xorMask(maskSize, 0x00);

            return CreateCursor(
                nullptr,
                0,
                0,
                width,
                height,
                andMask.data(),
                xorMask.data());
        }

        inline static constexpr std::array<DWORD, 16> systemCursorIds = {
            32512, // OCR_NORMAL
            32513, // Text select
            32514, // OCR_WAIT
            32515, // OCR_CROSS
            32516, // OCR_UP
            32642, // OCR_SIZENWSE
            32643, // OCR_SIZENESW
            32644, // OCR_SIZEWE
            32645, // OCR_SIZENS
            32646, // OCR_SIZEALL
            32648, // OCR_NO
            32649, // OCR_HAND
            32650, // Working in background
            32651, // OCR_HELP
            32671, // OCR_PIN
            32672, // OCR_PERSON
        };

        bool m_hidden = false;
    };
}
