// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma once

#include <Windows.h>
#include <objbase.h>

#include <algorithm>
#include <cstddef>
#include <cstring>
#include <limits>
#include <string>
#include <utility>
#include <vector>

namespace atomic_file
{
    namespace detail
    {
        struct temporary_file
        {
            explicit temporary_file(std::wstring filePath) :
                path(std::move(filePath))
            {
            }

            temporary_file(const temporary_file&) = delete;
            temporary_file& operator=(const temporary_file&) = delete;

            ~temporary_file()
            {
                if (handle != INVALID_HANDLE_VALUE)
                {
                    CloseHandle(handle);
                }
                if (ownsPath)
                {
                    DeleteFileW(path.c_str());
                }
            }

            std::wstring path;
            HANDLE handle = INVALID_HANDLE_VALUE;
            bool ownsPath = false;
        };

        template <typename SetFileInformationByHandleFn, typename MoveFileExFn>
        DWORD publish_temporary_file(
            HANDLE handle,
            const std::wstring& temporaryPath,
            const std::wstring& destination,
            SetFileInformationByHandleFn&& setFileInformationByHandle,
            MoveFileExFn&& moveFileEx)
        {
            const auto nameLength = destination.size() * sizeof(wchar_t);
            if (nameLength > (std::numeric_limits<DWORD>::max)() - sizeof(FILE_RENAME_INFO))
            {
                return ERROR_FILENAME_EXCED_RANGE;
            }

            std::vector<std::byte> renameBuffer(sizeof(FILE_RENAME_INFO) + nameLength);
            auto rename = reinterpret_cast<FILE_RENAME_INFO*>(renameBuffer.data());
            rename->Flags = FILE_RENAME_FLAG_REPLACE_IF_EXISTS | FILE_RENAME_FLAG_POSIX_SEMANTICS;
            rename->RootDirectory = nullptr;
            rename->FileNameLength = static_cast<DWORD>(nameLength);
            std::memcpy(rename->FileName, destination.data(), nameLength);

            constexpr DWORD retryDelays[] = { 10, 20, 40, 80, 160 };
            bool useMoveFileEx = false;
            size_t retry = 0;
            for (;;)
            {
                const auto succeeded = useMoveFileEx ?
                    moveFileEx(temporaryPath.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) :
                    setFileInformationByHandle(handle, FileRenameInfoEx, rename, static_cast<DWORD>(renameBuffer.size()));
                if (succeeded)
                {
                    return ERROR_SUCCESS;
                }

                const auto error = GetLastError();
                if (!useMoveFileEx &&
                    (error == ERROR_INVALID_FUNCTION ||
                     error == ERROR_INVALID_PARAMETER ||
                     error == ERROR_NOT_SUPPORTED ||
                     error == ERROR_CALL_NOT_IMPLEMENTED))
                {
                    useMoveFileEx = true;
                    continue;
                }
                if ((error != ERROR_SHARING_VIOLATION && error != ERROR_ACCESS_DENIED) || retry == ARRAYSIZE(retryDelays))
                {
                    return error;
                }

                Sleep(retryDelays[retry++]);
            }
        }
    }

    // Publish only complete, flushed contents. The destination is never opened for
    // writing: failures before the same-volume rename leave its previous contents intact.
    inline DWORD write(const std::wstring& destination, const std::string& contents)
    {
        GUID id{};
        if (FAILED(CoCreateGuid(&id)))
        {
            return ERROR_GEN_FAILURE;
        }

        wchar_t idString[39]{};
        if (!StringFromGUID2(id, idString, ARRAYSIZE(idString)))
        {
            return ERROR_GEN_FAILURE;
        }

        // A unique sibling allows simultaneous writers without sharing a temporary
        // file and keeps the final rename on the destination's volume.
        detail::temporary_file temporary(destination + L"." + idString + L".tmp");
        temporary.handle = CreateFileW(temporary.path.c_str(), GENERIC_WRITE | DELETE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (temporary.handle == INVALID_HANDLE_VALUE)
        {
            return GetLastError();
        }
        temporary.ownsPath = true;

        size_t offset = 0;
        while (offset < contents.size())
        {
            const auto bytesToWrite = static_cast<DWORD>((std::min)(contents.size() - offset, static_cast<size_t>((std::numeric_limits<DWORD>::max)())));
            DWORD bytesWritten = 0;
            if (!WriteFile(temporary.handle, contents.data() + offset, bytesToWrite, &bytesWritten, nullptr))
            {
                return GetLastError();
            }
            if (bytesWritten == 0)
            {
                return ERROR_WRITE_FAULT;
            }
            offset += bytesWritten;
        }

        if (!FlushFileBuffers(temporary.handle))
        {
            return GetLastError();
        }
        const auto result = detail::publish_temporary_file(
            temporary.handle,
            temporary.path,
            destination,
            [](HANDLE file, FILE_INFO_BY_HANDLE_CLASS infoClass, LPVOID info, DWORD size) {
                return SetFileInformationByHandle(file, infoClass, info, size);
            },
            [](LPCWSTR source, LPCWSTR target, DWORD flags) {
                return MoveFileExW(source, target, flags);
            });
        if (result == ERROR_SUCCESS)
        {
            temporary.ownsPath = false;
        }
        return result;
    }
}
