#pragma once

#include <Windows.h>
#include <winnetwk.h>

#include <string>
#include <string_view>

namespace copy_as_unc
{
    using DriveTypeResolver = UINT(WINAPI*)(LPCWSTR);
    using UniversalNameResolver = DWORD(WINAPI*)(LPCWSTR, DWORD, LPVOID, LPDWORD);
    using ClipboardWriter = HRESULT (*)(HWND, std::wstring_view);

    bool IsCopyablePath(std::wstring_view path, DriveTypeResolver getDriveType = GetDriveTypeW) noexcept;
    HRESULT ResolveToUNCPath(std::wstring_view path, std::wstring& uncPath, UniversalNameResolver resolveUniversalName = WNetGetUniversalNameW) noexcept;
    HRESULT WriteTextToClipboard(HWND owner, std::wstring_view text) noexcept;
    HRESULT ResolveAndCopyPath(
        std::wstring_view path,
        HWND owner,
        UniversalNameResolver resolveUniversalName = WNetGetUniversalNameW,
        ClipboardWriter writeToClipboard = WriteTextToClipboard) noexcept;
}
