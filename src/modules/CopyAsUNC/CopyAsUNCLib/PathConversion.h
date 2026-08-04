#pragma once

#include <Windows.h>
#include <winnetwk.h>

#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace copy_as_unc
{
    using UniversalNameResolver = std::function<DWORD(PCWSTR, DWORD, LPVOID, LPDWORD)>;

    inline std::optional<std::wstring> ResolvePath(std::wstring_view path, const UniversalNameResolver& resolver)
    {
        if (path.starts_with(L"\\\\"))
        {
            return std::wstring{ path };
        }

        const std::wstring localPath{ path };
        DWORD bufferSize = MAX_PATH * 2;
        std::vector<BYTE> buffer(bufferSize);
        DWORD result = resolver(localPath.c_str(), UNIVERSAL_NAME_INFO_LEVEL, buffer.data(), &bufferSize);

        if (result == ERROR_MORE_DATA)
        {
            buffer.resize(bufferSize);
            result = resolver(localPath.c_str(), UNIVERSAL_NAME_INFO_LEVEL, buffer.data(), &bufferSize);
        }

        if (result != NO_ERROR)
        {
            return std::nullopt;
        }

        const auto info = reinterpret_cast<const UNIVERSAL_NAME_INFOW*>(buffer.data());
        if (!info->lpUniversalName)
        {
            return std::nullopt;
        }

        return std::wstring{ info->lpUniversalName };
    }

    inline std::optional<std::wstring> ResolvePath(std::wstring_view path)
    {
        return ResolvePath(path, [](PCWSTR localPath, DWORD infoLevel, LPVOID buffer, LPDWORD bufferSize) {
            return WNetGetUniversalNameW(localPath, infoLevel, buffer, bufferSize);
        });
    }

    inline std::wstring BuildClipboardText(const std::vector<std::wstring>& paths, const UniversalNameResolver& resolver)
    {
        std::wstring clipboardText;
        for (const auto& path : paths)
        {
            const auto uncPath = ResolvePath(path, resolver);
            if (!uncPath)
            {
                continue;
            }

            if (!clipboardText.empty())
            {
                clipboardText += L"\r\n";
            }

            clipboardText += uncPath.value();
        }

        return clipboardText;
    }

    inline std::wstring BuildClipboardText(const std::vector<std::wstring>& paths)
    {
        return BuildClipboardText(paths, [](PCWSTR localPath, DWORD infoLevel, LPVOID buffer, LPDWORD bufferSize) {
            return WNetGetUniversalNameW(localPath, infoLevel, buffer, bufferSize);
        });
    }
}
