// Copyright (c) Microsoft Corporation
// Licensed under the MIT license.

#include "pch.h"

#include <Constants.h>
#include <FileConversionEngine.h>
#include <common/SettingsAPI/settings_objects.h>
#include <common/interop/pipe_caller_auth.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/base.h>
#include <common/logger/logger.h>
#include <common/utils/logger_helper.h>
#include <common/utils/package.h>
#include <common/utils/process_path.h>
#include <common/utils/shell_ext_registration.h>
#include <interface/powertoy_module_interface.h>

#include <algorithm>
#include <Aclapi.h>
#include <appmodel.h>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cwctype>
#include <filesystem>
#include <mutex>
#include <optional>
#include <queue>
#include <string>
#include <thread>
#include <unordered_set>
#include <vector>
#include <sddl.h>

extern "C" IMAGE_DOS_HEADER __ImageBase;
namespace winrt_json = winrt::Windows::Data::Json;
namespace fc_constants = winrt::PowerToys::FileConverter::Constants;

namespace
{
    constexpr wchar_t MODULE_NAME_FALLBACK[] = L"File Converter";
    constexpr wchar_t MODULE_KEY[] = L"FileConverter";
    constexpr wchar_t CONTEXT_MENU_PACKAGE_DISPLAY_NAME[] = L"FileConverterContextMenu";
    constexpr wchar_t CONTEXT_MENU_PACKAGE_FILE_NAME[] = L"FileConverterContextMenuPackage.msix";
    constexpr wchar_t CONTEXT_MENU_PACKAGE_FILE_PREFIX[] = L"FileConverterContextMenuPackage";
    constexpr wchar_t CONTEXT_MENU_HANDLER_CLSID[] = L"{57EC18F5-24D5-4DC6-AE2E-9D0F7A39F8BA}";
    constexpr size_t MAX_PIPE_PAYLOAD_BYTES = 1024 * 1024;
    constexpr wchar_t CONTEXT_MENU_ENABLED_VALUE[] = L"Enabled";
    constexpr wchar_t CONTEXT_MENU_PACKAGE_NAME[] = L"Microsoft.PowerToys.FileConverterContextMenu_";
    std::wstring LoadLocalizedString(std::wstring_view key, std::wstring_view fallback)
    {
        UNREFERENCED_PARAMETER(key);
        return std::wstring{ fallback };
    }

    struct ConversionRequest
    {
        file_converter::ImageFormat format = file_converter::ImageFormat::Png;
        std::vector<std::wstring> files;
        size_t skipped_entries = 0;
    };

    struct ConversionSummary
    {
        size_t succeeded = 0;
        size_t missing_inputs = 0;
        size_t failed = 0;
        std::wstring first_failed_path;
        std::wstring first_failed_error;
    };

    runtime_shell_ext::Spec BuildWin10ContextMenuSpec()
    {
        runtime_shell_ext::Spec spec;
        spec.clsid = CONTEXT_MENU_HANDLER_CLSID;
        spec.sentinelKey = L"Software\\Microsoft\\PowerToys\\FileConverter";
        spec.sentinelValue = L"ContextMenuRegisteredWin10";
        spec.dllFileCandidates = {
            L"WinUI3Apps\\PowerToys.FileConverterContextMenu.dll",
            L"PowerToys.FileConverterContextMenu.dll",
        };
        spec.friendlyName = L"File Converter Context Menu";
        spec.systemFileAssocHandlerName = L"FileConverterContextMenu";
        spec.representativeSystemExt = L".bmp";
        spec.systemFileAssocExtensions = {
            L".bmp",
            L".dib",
            L".gif",
            L".jfif",
            L".jpe",
            L".jpeg",
            L".jpg",
            L".jxr",
            L".png",
            L".tif",
            L".tiff",
            L".wdp",
            L".heic",
            L".heif",
            L".webp",
        };
        return spec;
    }

    std::optional<std::filesystem::path> FindLatestContextMenuPackage(const std::filesystem::path& context_menu_path)
    {
        const std::filesystem::path stable_package_path = context_menu_path / CONTEXT_MENU_PACKAGE_FILE_NAME;
        if (std::filesystem::exists(stable_package_path))
        {
            return stable_package_path;
        }

        std::vector<std::filesystem::path> candidate_packages;
        std::error_code ec;
        for (std::filesystem::directory_iterator it(context_menu_path, ec); !ec && it != std::filesystem::directory_iterator(); it.increment(ec))
        {
            if (!it->is_regular_file(ec))
            {
                continue;
            }

            const auto file_name = it->path().filename().wstring();
            const auto extension = it->path().extension().wstring();
            if (_wcsicmp(extension.c_str(), L".msix") != 0)
            {
                continue;
            }

            if (file_name.rfind(CONTEXT_MENU_PACKAGE_FILE_PREFIX, 0) == 0)
            {
                candidate_packages.push_back(it->path());
            }
        }

        if (candidate_packages.empty())
        {
            return std::nullopt;
        }

        std::sort(candidate_packages.begin(), candidate_packages.end(), [](const auto& lhs, const auto& rhs) {
            std::error_code lhs_ec;
            std::error_code rhs_ec;
            const auto lhs_time = std::filesystem::last_write_time(lhs, lhs_ec);
            const auto rhs_time = std::filesystem::last_write_time(rhs, rhs_ec);

            if (lhs_ec && rhs_ec)
            {
                return lhs.wstring() < rhs.wstring();
            }

            if (lhs_ec)
            {
                return true;
            }

            if (rhs_ec)
            {
                return false;
            }

            return lhs_time < rhs_time;
        });

        return candidate_packages.back();
    }

    std::wstring ToLower(std::wstring value)
    {
        std::transform(value.begin(), value.end(), value.begin(), [](wchar_t ch) {
            return static_cast<wchar_t>(towlower(ch));
        });

        return value;
    }

    std::optional<file_converter::ImageFormat> ParseFormat(const std::wstring& value)
    {
        const std::wstring lower = ToLower(value);

        if (lower == fc_constants::FormatPng)
        {
            return file_converter::ImageFormat::Png;
        }

        if (lower == fc_constants::FormatJpeg || lower == fc_constants::FormatJpg)
        {
            return file_converter::ImageFormat::Jpeg;
        }

        if (lower == fc_constants::FormatBmp)
        {
            return file_converter::ImageFormat::Bmp;
        }

        if (lower == fc_constants::FormatTiff || lower == fc_constants::FormatTif)
        {
            return file_converter::ImageFormat::Tiff;
        }

        if (lower == fc_constants::FormatHeic || lower == fc_constants::FormatHeif)
        {
            return file_converter::ImageFormat::Heif;
        }

        if (lower == fc_constants::FormatWebp)
        {
            return file_converter::ImageFormat::Webp;
        }

        return std::nullopt;
    }

    std::wstring ExtensionForFormat(file_converter::ImageFormat format)
    {
        switch (format)
        {
        case file_converter::ImageFormat::Jpeg:
            return fc_constants::ExtensionJpg;
        case file_converter::ImageFormat::Bmp:
            return fc_constants::ExtensionBmp;
        case file_converter::ImageFormat::Tiff:
            return fc_constants::ExtensionTiff;
        case file_converter::ImageFormat::Heif:
            return fc_constants::ExtensionHeic;
        case file_converter::ImageFormat::Webp:
            return fc_constants::ExtensionWebp;
        case file_converter::ImageFormat::Png:
        default:
            return fc_constants::ExtensionPng;
        }
    }

    std::wstring GetPipeNameForCurrentSession()
    {
        DWORD session_id = 0;
        if (!ProcessIdToSessionId(GetCurrentProcessId(), &session_id))
        {
            session_id = 0;
        }

        return std::wstring(fc_constants::PipeNamePrefix) + std::to_wstring(session_id);
    }

    struct PipeSecurity
    {
        SECURITY_ATTRIBUTES attributes{ sizeof(SECURITY_ATTRIBUTES), nullptr, FALSE };

        ~PipeSecurity()
        {
            if (attributes.lpSecurityDescriptor != nullptr)
            {
                LocalFree(attributes.lpSecurityDescriptor);
            }
        }

        bool initialize()
        {
            HANDLE token = nullptr;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
            {
                return false;
            }

            DWORD size = 0;
            GetTokenInformation(token, TokenUser, nullptr, 0, &size);
            std::vector<BYTE> buffer(size);
            if (!GetTokenInformation(token, TokenUser, buffer.data(), size, &size))
            {
                CloseHandle(token);
                return false;
            }
            CloseHandle(token);

            const auto token_user = reinterpret_cast<TOKEN_USER*>(buffer.data());
            LPWSTR sid = nullptr;
            if (!ConvertSidToStringSidW(token_user->User.Sid, &sid))
            {
                return false;
            }

            const std::wstring sddl = L"D:P(A;;GA;;;SY)(A;;GA;;;" + std::wstring(sid) + L")";
            LocalFree(sid);
            return ConvertStringSecurityDescriptorToSecurityDescriptorW(
                       sddl.c_str(),
                       SDDL_REVISION_1,
                       &attributes.lpSecurityDescriptor,
                       nullptr) != FALSE;
        }
    };

    std::string ReadPipeMessage(HANDLE pipe_handle, HANDLE stop_event)
    {
        constexpr DWORD BUFFER_SIZE = 4096;
        char buffer[BUFFER_SIZE] = {};
        std::string payload;

        while (payload.size() <= MAX_PIPE_PAYLOAD_BYTES)
        {
            HANDLE read_event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            OVERLAPPED overlapped{};
            overlapped.hEvent = read_event;

            DWORD bytes_read = 0;
            BOOL read_ok = ReadFile(pipe_handle, buffer, BUFFER_SIZE, &bytes_read, &overlapped);
            DWORD read_error = read_ok ? ERROR_SUCCESS : GetLastError();
            if (!read_ok && read_error == ERROR_IO_PENDING)
            {
                HANDLE events[] = { stop_event, read_event };
                const DWORD wait = WaitForMultipleObjects(ARRAYSIZE(events), events, FALSE, INFINITE);
                if (wait == WAIT_OBJECT_0)
                {
                    CancelIoEx(pipe_handle, &overlapped);
                    WaitForSingleObject(read_event, INFINITE);
                    DWORD ignored = 0;
                    GetOverlappedResult(pipe_handle, &overlapped, &ignored, FALSE);
                    CloseHandle(read_event);
                    return {};
                }

                read_ok = GetOverlappedResult(pipe_handle, &overlapped, &bytes_read, FALSE);
                read_error = read_ok ? ERROR_SUCCESS : GetLastError();
            }

            if (bytes_read > 0)
            {
                payload.append(buffer, bytes_read);
            }
            CloseHandle(read_event);

            if (read_ok || read_error == ERROR_BROKEN_PIPE || read_error == ERROR_PIPE_NOT_CONNECTED)
            {
                break;
            }

            if (read_error != ERROR_MORE_DATA)
            {
                Logger::warn(L"File Converter pipe read failed. Error={}", read_error);
                return {};
            }
        }

        return payload.size() <= MAX_PIPE_PAYLOAD_BYTES ? payload : std::string{};
    }

    bool IsExpectedPackagedSurrogate(HANDLE pipe_handle)
    {
        ULONG client_pid = 0;
        if (!GetNamedPipeClientProcessId(pipe_handle, &client_pid))
        {
            return false;
        }

        HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, client_pid);
        if (process == nullptr)
        {
            return false;
        }

        UINT32 length = 0;
        LONG result = GetPackageFullName(process, &length, nullptr);
        if (result != ERROR_INSUFFICIENT_BUFFER)
        {
            CloseHandle(process);
            return false;
        }

        std::wstring package_name(length, L'\0');
        result = GetPackageFullName(process, &length, package_name.data());
        CloseHandle(process);
        return result == ERROR_SUCCESS && package_name.rfind(CONTEXT_MENU_PACKAGE_NAME, 0) == 0;
    }

    bool TryParseFormatConvertRequest(
        const std::string& payload,
        ConversionRequest& request,
        std::wstring& rejection_reason)
    {
        request = {};
        rejection_reason.clear();

        if (payload.empty())
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_EmptyPayload", L"empty payload");
            return false;
        }

        winrt_json::JsonObject json_payload;
        if (!winrt_json::JsonObject::TryParse(winrt::to_hstring(payload), json_payload))
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_InvalidJson", L"invalid JSON");
            return false;
        }

        if (!json_payload.HasKey(fc_constants::JsonActionKey))
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_MissingAction", L"missing action");
            return false;
        }

        const auto action_value = json_payload.GetNamedValue(fc_constants::JsonActionKey);
        if (action_value.ValueType() != winrt_json::JsonValueType::String)
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_ActionNotString", L"action is not a string");
            return false;
        }

        const auto action = json_payload.GetNamedString(fc_constants::JsonActionKey);
        if (_wcsicmp(action.c_str(), fc_constants::ActionFormatConvert) != 0)
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_UnsupportedAction", L"unsupported action");
            return false;
        }

        std::wstring destination = fc_constants::FormatPng;
        if (json_payload.HasKey(fc_constants::JsonDestinationKey))
        {
            const auto destination_value = json_payload.GetNamedValue(fc_constants::JsonDestinationKey);
            if (destination_value.ValueType() != winrt_json::JsonValueType::String)
            {
                rejection_reason = LoadLocalizedString(L"FileConverter_Error_DestinationNotString", L"destination is not a string");
                return false;
            }

            destination = json_payload.GetNamedString(fc_constants::JsonDestinationKey).c_str();
        }

        if (!json_payload.HasKey(fc_constants::JsonFilesKey))
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_MissingFilesArray", L"missing files array");
            return false;
        }

        const auto files_value = json_payload.GetNamedValue(fc_constants::JsonFilesKey);
        if (files_value.ValueType() != winrt_json::JsonValueType::Array)
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_FilesNotArray", L"files is not an array");
            return false;
        }

        const auto files_array = json_payload.GetNamedArray(fc_constants::JsonFilesKey);
        for (const auto& file_value : files_array)
        {
            if (file_value.ValueType() != winrt_json::JsonValueType::String)
            {
                ++request.skipped_entries;
                continue;
            }

            const auto file_path = file_value.GetString();
            if (file_path.empty())
            {
                ++request.skipped_entries;
                continue;
            }

            request.files.push_back(file_path.c_str());
        }

        if (request.files.empty())
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_NoValidPaths", L"no valid file paths");
            return false;
        }

        const auto parsed_format = ParseFormat(destination);
        if (!parsed_format.has_value())
        {
            rejection_reason = LoadLocalizedString(L"FileConverter_Error_UnsupportedDestination", L"unsupported destination format");
            return false;
        }

        const auto support = file_converter::IsOutputFormatSupported(parsed_format.value());
        if (FAILED(support.hr))
        {
            rejection_reason = support.error_message.empty() ? LoadLocalizedString(L"FileConverter_Error_DestinationUnavailable", L"requested destination format is unavailable") : support.error_message;
            return false;
        }

        request.format = parsed_format.value();
        return true;
    }

    ConversionSummary ProcessFormatConvertRequest(const ConversionRequest& request)
    {
        ConversionSummary summary;
        const std::wstring output_extension = ExtensionForFormat(request.format);
        std::unordered_set<std::wstring> seen_files;

        for (const auto& file : request.files)
        {
            if (!seen_files.insert(file).second)
            {
                continue;
            }

            const std::filesystem::path input_path(file);
            std::error_code ec;
            if (input_path.empty() || !std::filesystem::exists(input_path, ec) || ec)
            {
                ++summary.missing_inputs;
                continue;
            }

            ec.clear();
            if (!std::filesystem::is_regular_file(input_path, ec) || ec)
            {
                ++summary.missing_inputs;
                continue;
            }

            std::filesystem::path output_path;
            for (unsigned int suffix = 0;; ++suffix)
            {
                output_path = input_path.parent_path() / input_path.stem();
                output_path += suffix == 0 ? L"_converted" : L"_converted_" + std::to_wstring(suffix);
                output_path += output_extension;
                if (!std::filesystem::exists(output_path, ec))
                {
                    break;
                }
                ec.clear();
            }

            const auto conversion = file_converter::ConvertImageFile(input_path.wstring(), output_path.wstring(), request.format);
            if (conversion.succeeded())
            {
                ++summary.succeeded;
                continue;
            }

            ++summary.failed;
            if (summary.first_failed_path.empty())
            {
                summary.first_failed_path = input_path.wstring();
                summary.first_failed_error = conversion.error_message;
            }
        }

        return summary;
    }

    void EnsureContextMenuPackageRegistered()
    {
        if (!package::IsWin11OrGreater())
        {
            return;
        }

        const std::filesystem::path module_path = get_module_folderpath(reinterpret_cast<HMODULE>(&__ImageBase));
        const std::filesystem::path context_menu_path = module_path / L"WinUI3Apps";
        if (!std::filesystem::exists(context_menu_path))
        {
            return;
        }

        const auto package_path = FindLatestContextMenuPackage(context_menu_path);
        if (!package_path.has_value())
        {
            return;
        }

        if (!package::IsPackageRegisteredWithPowerToysVersion(CONTEXT_MENU_PACKAGE_DISPLAY_NAME))
        {
            (void)package::RegisterSparsePackage(context_menu_path.wstring(), package_path->wstring());
        }
    }

    void EnsureContextMenuRuntimeRegistered()
    {
        if (package::IsWin11OrGreater())
        {
            return;
        }

        (void)runtime_shell_ext::EnsureRegistered(BuildWin10ContextMenuSpec(), reinterpret_cast<HMODULE>(&__ImageBase));
    }

    void UnregisterContextMenuRuntime()
    {
        if (package::IsWin11OrGreater())
        {
            return;
        }

        runtime_shell_ext::Unregister(BuildWin10ContextMenuSpec());
    }

    void SetContextMenuEnabledState(bool enabled)
    {
        HKEY key = nullptr;
        if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\PowerToys\\FileConverter", 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) == ERROR_SUCCESS)
        {
            const DWORD value = enabled ? 1 : 0;
            RegSetValueExW(key, CONTEXT_MENU_ENABLED_VALUE, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&value), sizeof(value));
            RegCloseKey(key);
        }
    }

    bool IsCurrentProcessElevated()
    {
        HANDLE token = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
        {
            return true;
        }

        TOKEN_ELEVATION elevation{};
        DWORD size = 0;
        bool elevated = true;
        if (GetTokenInformation(token, TokenElevation, &elevation, sizeof(elevation), &size))
        {
            elevated = elevation.TokenIsElevated != 0;
        }
        CloseHandle(token);
        return elevated;
    }

    class FileConverterPipeOrchestrator
    {
    public:
        void Start(const std::wstring& pipe_name)
        {
            if (m_running.exchange(true))
            {
                return;
            }

            m_pipe_name = pipe_name;
            ResetEvent(m_stop_event);
            m_listener_thread = std::thread(&FileConverterPipeOrchestrator::ListenerLoop, this);
            m_worker_thread = std::thread(&FileConverterPipeOrchestrator::WorkerLoop, this);
        }

        void Stop()
        {
            if (!m_running.exchange(false))
            {
                return;
            }

            SetEvent(m_stop_event);
            m_queue_cv.notify_all();

            if (m_listener_thread.joinable())
            {
                m_listener_thread.join();
            }

            if (m_worker_thread.joinable())
            {
                m_worker_thread.join();
            }

            std::queue<std::string> empty;
            {
                std::scoped_lock lock(m_queue_mutex);
                std::swap(m_pending_payloads, empty);
            }
        }

        void EnqueueActionPayload(std::string payload)
        {
            if (!m_running.load())
            {
                return;
            }

            EnqueuePayload(std::move(payload));
        }

        ~FileConverterPipeOrchestrator()
        {
            Stop();
            CloseHandle(m_stop_event);
        }

    private:
        void EnqueuePayload(std::string payload)
        {
            {
                std::scoped_lock lock(m_queue_mutex);
                if (!m_running.load())
                {
                    return;
                }

                m_pending_payloads.push(std::move(payload));
            }

            m_queue_cv.notify_one();
        }

        void ProcessPayload(const std::string& payload)
        {
            ConversionRequest request;
            std::wstring rejection_reason;
            if (!TryParseFormatConvertRequest(payload, request, rejection_reason))
            {
                if (!rejection_reason.empty())
                {
                    Logger::warn(L"File Converter ignored malformed request: {}", rejection_reason);
                }

                return;
            }

            const auto summary = ProcessFormatConvertRequest(request);

            if (request.skipped_entries > 0)
            {
                Logger::warn(L"File Converter request skipped {} invalid file entries.", request.skipped_entries);
            }

            if (summary.missing_inputs > 0)
            {
                Logger::warn(L"File Converter request skipped {} missing input files.", summary.missing_inputs);
            }

            if (summary.failed > 0)
            {
                Logger::warn(L"File Converter conversion failed for {} file(s).", summary.failed);
                if (!summary.first_failed_path.empty())
                {
                    Logger::warn(L"First conversion failure: path='{}' reason='{}'", summary.first_failed_path, summary.first_failed_error);
                }
            }
        }

        void WorkerLoop()
        {
            while (true)
            {
                std::string payload;
                {
                    std::unique_lock lock(m_queue_mutex);
                    m_queue_cv.wait(lock, [this] {
                        return !m_running.load() || !m_pending_payloads.empty();
                    });

                    if (!m_running.load())
                    {
                        break;
                    }

                    if (m_pending_payloads.empty())
                    {
                        continue;
                    }

                    payload = std::move(m_pending_payloads.front());
                    m_pending_payloads.pop();
                }

                ProcessPayload(payload);
            }
        }

        void ListenerLoop()
        {
            PipeSecurity security;
            if (!security.initialize())
            {
                Logger::error(L"File Converter could not initialize pipe security.");
                return;
            }

            HANDLE pipe_handle = CreateNamedPipeW(
                m_pipe_name.c_str(),
                PIPE_ACCESS_INBOUND | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
                PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                1,
                0,
                4096,
                0,
                &security.attributes);

            if (pipe_handle == INVALID_HANDLE_VALUE)
            {
                Logger::error(L"File Converter could not create its named pipe. Error={}", GetLastError());
                return;
            }

            while (m_running.load())
            {
                HANDLE connect_event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
                OVERLAPPED connect_overlapped{};
                connect_overlapped.hEvent = connect_event;
                BOOL connected = ConnectNamedPipe(pipe_handle, &connect_overlapped);
                DWORD connect_error = connected ? ERROR_SUCCESS : GetLastError();
                if (!connected && connect_error == ERROR_IO_PENDING)
                {
                    HANDLE events[] = { m_stop_event, connect_event };
                    const DWORD wait = WaitForMultipleObjects(ARRAYSIZE(events), events, FALSE, INFINITE);
                    if (wait == WAIT_OBJECT_0)
                    {
                        CancelIoEx(pipe_handle, &connect_overlapped);
                        WaitForSingleObject(connect_event, INFINITE);
                        DWORD ignored = 0;
                        GetOverlappedResult(pipe_handle, &connect_overlapped, &ignored, FALSE);
                        CloseHandle(connect_event);
                        break;
                    }
                    DWORD transferred = 0;
                    connected = GetOverlappedResult(pipe_handle, &connect_overlapped, &transferred, FALSE);
                }
                else if (!connected && connect_error == ERROR_PIPE_CONNECTED)
                {
                    connected = TRUE;
                }

                if (!connected)
                {
                    CloseHandle(connect_event);
                    DisconnectNamedPipe(pipe_handle);
                    if (m_running.load())
                    {
                        std::this_thread::sleep_for(std::chrono::milliseconds(50));
                    }
                    continue;
                }
                CloseHandle(connect_event);

                interop_auth::CallerPolicy policy;
                policy.enabled = true;
                wchar_t caller_directory[MAX_PATH]{};
                bool test_client_mode = false;
#ifdef _DEBUG
                wchar_t test_client_directory[MAX_PATH]{};
                if (GetEnvironmentVariableW(L"POWERTOYS_FILECONVERTER_TEST_CLIENT_DIR", test_client_directory, ARRAYSIZE(test_client_directory)) > 0)
                {
                    test_client_mode = true;
                    policy.expectedDirectory = test_client_directory;
                    policy.allowedBasenames = { L"powershell.exe", L"pwsh.exe" };
                    policy.requireMicrosoftSignature = false;
                }
                else
#endif
                if (package::IsWin11OrGreater())
                {
                    GetSystemDirectoryW(caller_directory, ARRAYSIZE(caller_directory));
                    policy.expectedDirectory = caller_directory;
                    policy.allowedBasenames = { L"dllhost.exe" };
                    policy.requireMicrosoftSignature = true;
                }
                else
                {
                    GetWindowsDirectoryW(caller_directory, ARRAYSIZE(caller_directory));
                    policy.expectedDirectory = caller_directory;
                    policy.allowedBasenames = { L"explorer.exe" };
                    policy.requireMicrosoftSignature = true;
                }
                policy.logReject = [](const interop_auth::AuthResult& result) {
                    Logger::warn(L"File Converter rejected pipe caller pid={} path='{}' reason='{}'", result.pid, result.imagePath, result.reasonCode);
                };
                const auto auth = interop_auth::AuthenticateClient(pipe_handle, policy, m_auth_cache);
                const bool package_identity_valid = test_client_mode || !package::IsWin11OrGreater() || IsExpectedPackagedSurrogate(pipe_handle);
                if (auth.accepted && !package_identity_valid)
                {
                    Logger::warn(L"File Converter rejected pipe caller with an unexpected package identity.");
                }
                const std::string payload = auth.accepted && package_identity_valid ? ReadPipeMessage(pipe_handle, m_stop_event) : std::string{};

                // Inbound-only server pipes have no outbound data to flush.
                // Skipping FlushFileBuffers avoids reconnect stalls on malformed-request sequences.
                DisconnectNamedPipe(pipe_handle);

                if (!m_running.load())
                {
                    break;
                }

                if (!payload.empty())
                {
                    EnqueuePayload(payload);
                }
            }

            CloseHandle(pipe_handle);
        }

        std::atomic<bool> m_running = false;
        HANDLE m_stop_event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        interop_auth::VerificationCache m_auth_cache;
        std::wstring m_pipe_name;
        std::thread m_listener_thread;
        std::thread m_worker_thread;
        std::mutex m_queue_mutex;
        std::condition_variable m_queue_cv;
        std::queue<std::string> m_pending_payloads;
    };
}

class FileConverterModule : public PowertoyModuleIface
{
public:
    FileConverterModule()
    {
        // Avoid WinRT resource activation during module construction.
        // The runner loads modules very early, and constructor failures can terminate startup.
        m_name = MODULE_NAME_FALLBACK;
        LoggerHelpers::init_logger(m_key, L"ModuleInterface", "fileconverter");
    }

    ~FileConverterModule()
    {
        disable();
    }

    void destroy() override
    {
        delete this;
    }

    const wchar_t* get_name() override
    {
        return m_name.c_str();
    }

    const wchar_t* get_key() override
    {
        return m_key.c_str();
    }

    bool get_config(wchar_t* buffer, int* buffer_size) override
    {
        HINSTANCE hinstance = reinterpret_cast<HINSTANCE>(&__ImageBase);
        PowerToysSettings::Settings settings(hinstance, get_name());
        settings.set_description(LoadLocalizedString(L"FileConverter_Settings_Description", L"Convert image files to common formats."));
        settings.set_overview_link(L"https://aka.ms/PowerToysOverview_FileConverter");
        settings.set_icon_key(L"pt-file-converter");
        return settings.serialize_to_buffer(buffer, buffer_size);
    }

    void set_config(const wchar_t* /*config*/) override
    {
    }

    void call_custom_action(const wchar_t* action) override
    {
        if (action == nullptr)
        {
            return;
        }

        m_pipe_orchestrator.EnqueueActionPayload(winrt::to_string(action));
    }

    void enable() override
    {
        if (m_enabled)
        {
            return;
        }

        EnsureContextMenuPackageRegistered();
        EnsureContextMenuRuntimeRegistered();
        const bool can_convert_safely = !IsCurrentProcessElevated();
        SetContextMenuEnabledState(can_convert_safely);
        if (can_convert_safely)
        {
            m_pipe_orchestrator.Start(GetPipeNameForCurrentSession());
        }
        else
        {
            Logger::warn(L"File Converter is unavailable while PowerToys is running elevated.");
        }
        m_enabled = true;
    }

    void disable() override
    {
        if (!m_enabled)
        {
            return;
        }

        m_pipe_orchestrator.Stop();
        SetContextMenuEnabledState(false);
        UnregisterContextMenuRuntime();
        m_enabled = false;
    }

    bool is_enabled() override
    {
        return m_enabled;
    }

    bool is_enabled_by_default() const override
    {
        return false;
    }

private:
    bool m_enabled = false;
    std::wstring m_name;
    std::wstring m_key = MODULE_KEY;
    FileConverterPipeOrchestrator m_pipe_orchestrator;
};

extern "C" __declspec(dllexport) PowertoyModuleIface* __cdecl powertoy_create()
{
    return new FileConverterModule();
}
