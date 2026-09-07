#include "pch.h"
#include "PathUtils.h"

#include <cwctype>
#include <cstring>
#include <vector>

namespace
{
    constexpr DWORD MaxUniversalNameBufferSize = sizeof(UNIVERSAL_NAME_INFOW) + (32768 * sizeof(wchar_t));

    bool IsExtendedUNCPath(std::wstring_view path) noexcept
    {
        return path.size() >= 8 &&
               path[0] == L'\\' &&
               path[1] == L'\\' &&
               path[2] == L'?' &&
               path[3] == L'\\' &&
               towupper(path[4]) == L'U' &&
               towupper(path[5]) == L'N' &&
               towupper(path[6]) == L'C' &&
               path[7] == L'\\';
    }

    bool IsUNCPath(std::wstring_view path) noexcept
    {
        if (IsExtendedUNCPath(path))
        {
            return true;
        }

        return path.size() >= 3 &&
               path[0] == L'\\' &&
               path[1] == L'\\' &&
               path[2] != L'?' &&
               path[2] != L'.';
    }

    constexpr bool IsDevicePath(std::wstring_view path) noexcept
    {
        return path.size() >= 4 &&
               path[0] == L'\\' &&
               path[1] == L'\\' &&
               (path[2] == L'?' || path[2] == L'.') &&
               path[3] == L'\\';
    }

    HRESULT LastErrorToHRESULT(DWORD fallback)
    {
        const DWORD error = GetLastError();
        return HRESULT_FROM_WIN32(error == ERROR_SUCCESS ? fallback : error);
    }
}

namespace copy_as_unc
{
    bool IsCopyablePath(std::wstring_view path, DriveTypeResolver getDriveType) noexcept
    {
        if (IsUNCPath(path))
        {
            return true;
        }

        if (!getDriveType || path.size() < 3 || path[1] != L':' || (path[2] != L'\\' && path[2] != L'/'))
        {
            return false;
        }

        const wchar_t root[] = { path[0], L':', L'\\', L'\0' };
        return getDriveType(root) == DRIVE_REMOTE;
    }

    HRESULT ResolveToUNCPath(std::wstring_view path, std::wstring& uncPath, UniversalNameResolver resolveUniversalName) noexcept
    try
    {
        uncPath.clear();
        if (path.empty() || !resolveUniversalName)
        {
            return E_INVALIDARG;
        }

        if (IsUNCPath(path))
        {
            uncPath.assign(path);
            return S_OK;
        }

        if (IsDevicePath(path))
        {
            return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        }

        const std::wstring nullTerminatedPath{ path };
        DWORD bufferSize = sizeof(UNIVERSAL_NAME_INFOW) + ((MAX_PATH + 1) * sizeof(wchar_t));
        std::vector<BYTE> buffer(bufferSize);

        DWORD result = resolveUniversalName(
            nullTerminatedPath.c_str(),
            UNIVERSAL_NAME_INFO_LEVEL,
            buffer.data(),
            &bufferSize);

        if (result == ERROR_MORE_DATA)
        {
            if (bufferSize < sizeof(UNIVERSAL_NAME_INFOW) || bufferSize > MaxUniversalNameBufferSize)
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }

            buffer.resize(bufferSize);
            result = resolveUniversalName(
                nullTerminatedPath.c_str(),
                UNIVERSAL_NAME_INFO_LEVEL,
                buffer.data(),
                &bufferSize);
        }

        if (result != NO_ERROR)
        {
            return HRESULT_FROM_WIN32(result);
        }

        const auto info = reinterpret_cast<const UNIVERSAL_NAME_INFOW*>(buffer.data());
        if (!info->lpUniversalName)
        {
            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        }

        uncPath.assign(info->lpUniversalName);
        return S_OK;
    }
    catch (const std::bad_alloc&)
    {
        return E_OUTOFMEMORY;
    }
    catch (...)
    {
        return E_FAIL;
    }

    HRESULT WriteTextToClipboard(HWND owner, std::wstring_view text) noexcept
    {
        if (text.empty())
        {
            return E_INVALIDARG;
        }

        const size_t byteLength = (text.size() + 1) * sizeof(wchar_t);
        HGLOBAL memory = GlobalAlloc(GMEM_MOVEABLE, byteLength);
        if (!memory)
        {
            return LastErrorToHRESULT(ERROR_OUTOFMEMORY);
        }

        void* locked = GlobalLock(memory);
        if (!locked)
        {
            const HRESULT result = LastErrorToHRESULT(ERROR_LOCK_FAILED);
            GlobalFree(memory);
            return result;
        }

        std::memcpy(locked, text.data(), text.size() * sizeof(wchar_t));
        static_cast<wchar_t*>(locked)[text.size()] = L'\0';
        GlobalUnlock(memory);

        bool clipboardOpened = false;
        for (int attempt = 0; attempt < 5 && !clipboardOpened; ++attempt)
        {
            clipboardOpened = OpenClipboard(owner) != FALSE;
            if (!clipboardOpened && attempt < 4)
            {
                Sleep(10);
            }
        }

        if (!clipboardOpened)
        {
            const HRESULT result = LastErrorToHRESULT(ERROR_ACCESS_DENIED);
            GlobalFree(memory);
            return result;
        }

        if (!EmptyClipboard())
        {
            const HRESULT result = LastErrorToHRESULT(ERROR_ACCESS_DENIED);
            CloseClipboard();
            GlobalFree(memory);
            return result;
        }

        if (!SetClipboardData(CF_UNICODETEXT, memory))
        {
            const HRESULT result = LastErrorToHRESULT(ERROR_ACCESS_DENIED);
            CloseClipboard();
            GlobalFree(memory);
            return result;
        }

        memory = nullptr;
        if (!CloseClipboard())
        {
            return LastErrorToHRESULT(ERROR_ACCESS_DENIED);
        }

        return S_OK;
    }

    HRESULT ResolveAndCopyPath(
        std::wstring_view path,
        HWND owner,
        UniversalNameResolver resolveUniversalName,
        ClipboardWriter writeToClipboard) noexcept
    {
        if (!writeToClipboard)
        {
            return E_INVALIDARG;
        }

        std::wstring uncPath;
        const HRESULT result = ResolveToUNCPath(path, uncPath, resolveUniversalName);
        if (FAILED(result))
        {
            return result;
        }

        return writeToClipboard(owner, uncPath);
    }
}
