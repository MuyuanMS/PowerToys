// Copyright (c) Microsoft Corporation
// Licensed under the MIT license.

#include "PTSettingsClient.h"
#include "../WorkspacesSettingsService/protocol/Protocol.h"
#include "../WorkspacesSettingsService/protocol/PipeName.h"

#include <windows.h>
#include <shlobj.h>
#include <vector>
#include <cstring>
#include <string>

#pragma comment(lib, "Advapi32.lib")
#pragma comment(lib, "Shell32.lib")
#pragma comment(lib, "Ole32.lib")
namespace PTSettingsClient
{
    namespace
    {
        using PTSettingsSvc::kMaxPayloadBytes;
        using PTSettingsSvc::Opcode;
        using PTSettingsSvc::Status;
        constexpr const wchar_t* kServiceExeName = L"PowerToys.PTSettingsSvc.exe";

        // This client reaches ITS OWN user's service instance, whose pipe is
        // \\.\pipe\PTSettingsSvc_<SID> where <SID> is our own token SID
        //.  Computed once per process.
        const std::wstring& OwnPipeName()
        {
            static const std::wstring name =
                PTSettingsSvc::BuildPipeName(PTSettingsSvc::CurrentProcessUserSidString());
            return name;
        }

        struct PipeHandle
        {
            HANDLE h = INVALID_HANDLE_VALUE;
            ~PipeHandle()
            {
                if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
            }
        };

        std::wstring CurrentProcessVersion()
        {
            wchar_t path[MAX_PATH * 2] = {};
            DWORD length = GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
            if (length == 0 || length >= ARRAYSIZE(path))
            {
                return {};
            }

            HMODULE module = LoadLibraryExW(
                path,
                nullptr,
                LOAD_LIBRARY_AS_IMAGE_RESOURCE | LOAD_LIBRARY_AS_DATAFILE);
            if (!module)
            {
                return {};
            }

            VS_FIXEDFILEINFO fixedInfo{};
            bool found = false;
            if (HRSRC resource =
                    FindResourceW(module, MAKEINTRESOURCEW(1), RT_VERSION))
            {
                if (HGLOBAL loaded = LoadResource(module, resource))
                {
                    const void* data = LockResource(loaded);
                    const DWORD size = SizeofResource(module, resource);
                    if (data && size >= sizeof(VS_FIXEDFILEINFO))
                    {
                        const BYTE* bytes = static_cast<const BYTE*>(data);
                        for (size_t offset = 0;
                             offset + sizeof(VS_FIXEDFILEINFO) <= size;
                             offset += sizeof(DWORD))
                        {
                            memcpy(&fixedInfo,
                                   bytes + offset,
                                   sizeof(fixedInfo));
                            if (fixedInfo.dwSignature == 0xFEEF04BD)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                }
            }
            FreeLibrary(module);

            if (!found)
            {
                return {};
            }

            wchar_t version[64] = {};
            swprintf_s(version,
                       L"%u.%u.%u.%u",
                       HIWORD(fixedInfo.dwFileVersionMS),
                       LOWORD(fixedInfo.dwFileVersionMS),
                       HIWORD(fixedInfo.dwFileVersionLS),
                       LOWORD(fixedInfo.dwFileVersionLS));
            return version;
        }

        std::wstring ExpectedServerPath()
        {
            PWSTR programData = nullptr;
            if (FAILED(SHGetKnownFolderPath(FOLDERID_ProgramData, 0, nullptr, &programData)))
            {
                return {};
            }

            std::wstring result(programData);
            CoTaskMemFree(programData);

            const std::wstring sid = PTSettingsSvc::CurrentProcessUserSidString();
            const std::wstring version = CurrentProcessVersion();
            if (sid.empty() || version.empty())
            {
                return {};
            }

            return result + L"\\Microsoft\\PowerToys\\SettingsSvcBin\\" +
                   sid + L"\\" + version + L"\\" + kServiceExeName;
        }

        bool ServerPidMatchesRegisteredService(ULONG serverPid,
                                               const std::wstring& userSid)
        {
            SC_HANDLE scm = OpenSCManagerW(
                nullptr,
                nullptr,
                SC_MANAGER_CONNECT);
            if (!scm)
            {
                return false;
            }

            const std::wstring serviceName =
                L"PTSettingsSvc_" + userSid;
            SC_HANDLE service = OpenServiceW(
                scm,
                serviceName.c_str(),
                SERVICE_QUERY_STATUS);
            if (!service)
            {
                CloseServiceHandle(scm);
                return false;
            }

            SERVICE_STATUS_PROCESS status{};
            DWORD needed = 0;
            const bool matched =
                QueryServiceStatusEx(
                    service,
                    SC_STATUS_PROCESS_INFO,
                    reinterpret_cast<BYTE*>(&status),
                    sizeof(status),
                    &needed) &&
                status.dwCurrentState != SERVICE_STOPPED &&
                status.dwProcessId == serverPid;
            CloseServiceHandle(service);
            CloseServiceHandle(scm);
            return matched;
        }

        bool IsTrustedServer(HANDLE pipe)
        {
            ULONG serverPid = 0;
            if (!GetNamedPipeServerProcessId(pipe, &serverPid))
            {
                return false;
            }

            HANDLE process =
                OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, serverPid);
            if (!process)
            {
                return false;
            }

            wchar_t imagePath[4096] = {};
            DWORD imagePathLength = ARRAYSIZE(imagePath);
            const bool gotPath =
                QueryFullProcessImageNameW(process,
                                           0,
                                           imagePath,
                                           &imagePathLength) != FALSE;

            const std::wstring expectedPath = ExpectedServerPath();
            const std::wstring userSid =
                PTSettingsSvc::CurrentProcessUserSidString();
            const bool trusted =
                gotPath &&
                !expectedPath.empty() &&
                _wcsicmp(imagePath, expectedPath.c_str()) == 0 &&
                ServerPidMatchesRegisteredService(serverPid, userSid);

            CloseHandle(process);
            return trusted;
        }

        bool Connect(PipeHandle& out)
        {
            const std::wstring& pipeName = OwnPipeName();
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                HANDLE h = CreateFileW(pipeName.c_str(),
                                       GENERIC_READ | GENERIC_WRITE,
                                       0,
                                       nullptr,
                                       OPEN_EXISTING,
                                       // Allow the server to impersonate us
                                       // so it can read our SID; anything
                                       // weaker yields an Anonymous token
                                       // and the server's auth check fails.
                                       SECURITY_SQOS_PRESENT | SECURITY_IMPERSONATION,
                                       nullptr);
                if (h != INVALID_HANDLE_VALUE)
                {
                    if (!IsTrustedServer(h))
                    {
                        CloseHandle(h);
                        return false;
                    }
                    out.h = h;
                    return true;
                }
                DWORD err = GetLastError();
                if (err != ERROR_PIPE_BUSY && err != ERROR_FILE_NOT_FOUND)
                {
                    return false;
                }
                WaitNamedPipeW(pipeName.c_str(), 2000);
            }
            return false;
        }

        bool WriteAll(HANDLE h, const void* buf, DWORD len)
        {
            const BYTE* p = static_cast<const BYTE*>(buf);
            while (len > 0)
            {
                DWORD wrote = 0;
                if (!WriteFile(h, p, len, &wrote, nullptr) || wrote == 0) return false;
                p += wrote;
                len -= wrote;
            }
            return true;
        }

        bool ReadAll(HANDLE h, void* buf, DWORD len)
        {
            BYTE* p = static_cast<BYTE*>(buf);
            while (len > 0)
            {
                DWORD got = 0;
                if (!ReadFile(h, p, len, &got, nullptr) || got == 0) return false;
                p += got;
                len -= got;
            }
            return true;
        }

        Result MapStatus(Status s)
        {
            switch (s)
            {
            case Status::Ok:               return Result::Ok;
            case Status::AuthFailToken:
            case Status::AuthFailCaller:   return Result::AuthRejected;
            case Status::NamespaceUnknown: return Result::NamespaceUnknown;
            case Status::BadRequest:
            case Status::UnknownOpcode:    return Result::ProtocolError;
            case Status::PayloadTooLarge:  return Result::PayloadTooLarge;
            case Status::NotFound:         return Result::NotFound;
            case Status::IoError:          return Result::IoError;
            }
            return Result::UnknownStatus;
        }

        Result RoundTrip(Opcode op, const void* payload, uint32_t payloadLen,
                         std::vector<uint8_t>& outResp)
        {
            outResp.clear();
            if (payloadLen > kMaxPayloadBytes)
            {
                return Result::PayloadTooLarge;
            }

            PipeHandle pipe;
            if (!Connect(pipe))
            {
                return Result::ServiceUnavailable;
            }

            uint8_t opByte = static_cast<uint8_t>(op);
            if (!WriteAll(pipe.h, &opByte, sizeof(opByte)) ||
                !WriteAll(pipe.h, &payloadLen, sizeof(payloadLen)) ||
                (payloadLen > 0 && !WriteAll(pipe.h, payload, payloadLen)))
            {
                return Result::ProtocolError;
            }

            uint8_t statusByte = 0;
            uint32_t respLen = 0;
            if (!ReadAll(pipe.h, &statusByte, sizeof(statusByte)) ||
                !ReadAll(pipe.h, &respLen, sizeof(respLen)))
            {
                return Result::ProtocolError;
            }
            if (respLen > kMaxPayloadBytes)
            {
                return Result::ProtocolError;
            }
            if (respLen > 0)
            {
                outResp.resize(respLen);
                if (!ReadAll(pipe.h, outResp.data(), respLen))
                {
                    outResp.clear();
                    return Result::ProtocolError;
                }
            }
            constexpr uint8_t ResponseAck = 0xA5;
            WriteAll(pipe.h, &ResponseAck, sizeof(ResponseAck));
            return MapStatus(static_cast<Status>(statusByte));
        }
    }

    Result Ping()
    {
        std::vector<uint8_t> resp;
        return RoundTrip(Opcode::Ping, nullptr, 0, resp);
    }

    Result GetBlob(std::vector<uint8_t>& outBytes)
    {
        return RoundTrip(Opcode::GetBlob, nullptr, 0, outBytes);
    }

    Result PutBlob(const std::vector<uint8_t>& bytes)
    {
        std::vector<uint8_t> resp;
        return RoundTrip(Opcode::PutBlob,
                         bytes.data(),
                         static_cast<uint32_t>(bytes.size()),
                         resp);
    }
}
