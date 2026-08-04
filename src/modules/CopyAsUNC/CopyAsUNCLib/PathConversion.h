#pragma once

#include <Windows.h>
#include <winnetwk.h>

#include <cwctype>
#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace copy_as_unc
{
    using UniversalNameResolver = std::function<DWORD(PCWSTR, DWORD, LPVOID, LPDWORD)>;

    inline bool StartsWithInsensitive(std::wstring_view value, std::wstring_view prefix)
    {
        if (value.size() < prefix.size())
        {
            return false;
        }

        for (size_t index = 0; index < prefix.size(); ++index)
        {
            if (std::towlower(value[index]) != std::towlower(prefix[index]))
            {
                return false;
            }
        }

        return true;
    }

    inline std::optional<std::wstring> ResolvePath(std::wstring_view path, const UniversalNameResolver& resolver)
    {
        constexpr std::wstring_view extendedUncPrefix = L"\\\\?\\UNC\\";
        if (StartsWithInsensitive(path, extendedUncPrefix))
        {
            return L"\\\\" + std::wstring{ path.substr(extendedUncPrefix.size()) };
        }

        if (path.starts_with(L"\\\\") && !path.starts_with(L"\\\\?\\") && !path.starts_with(L"\\\\.\\"))
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
