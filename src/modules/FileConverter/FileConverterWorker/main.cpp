#include "pch.h"

#include "../FileConverterLib/Constants.h"
#include "../FileConverterLib/FileConversionEngine.h"
#include <common/logger/logger.h>
#include <common/utils/logger_helper.h>

namespace
{
    constexpr DWORD ENCODER_PROBE_TIMEOUT_MS = 10000;

    std::wstring ExtensionForFormat(file_converter::ImageFormat format)
    {
        switch (format)
        {
        case file_converter::ImageFormat::Jpeg:
            return L".jpg";
        case file_converter::ImageFormat::Bmp:
            return L".bmp";
        case file_converter::ImageFormat::Tiff:
            return L".tiff";
        case file_converter::ImageFormat::Heif:
            return L".heic";
        case file_converter::ImageFormat::Webp:
            return L".webp";
        default:
            return L".png";
        }
    }

    std::optional<file_converter::ImageFormat> ParseFormat(const wchar_t* value)
    {
        try
        {
            const int format = std::stoi(value);
            if (format < static_cast<int>(file_converter::ImageFormat::Png) ||
                format > static_cast<int>(file_converter::ImageFormat::Webp))
            {
                return std::nullopt;
            }

            return static_cast<file_converter::ImageFormat>(format);
        }
        catch (...)
        {
            return std::nullopt;
        }
    }

    bool ProbeFormatInChildProcess(
        const std::filesystem::path& worker_path,
        file_converter::ImageFormat format)
    {
        std::wstring command_line =
            L"\"" + worker_path.wstring() + L"\" --probe " +
            std::to_wstring(static_cast<int>(format));
        STARTUPINFOW startup_info{};
        startup_info.cb = sizeof(startup_info);
        PROCESS_INFORMATION process_info{};
        if (!CreateProcessW(
                worker_path.c_str(),
                command_line.data(),
                nullptr,
                nullptr,
                FALSE,
                CREATE_NO_WINDOW,
                nullptr,
                worker_path.parent_path().c_str(),
                &startup_info,
                &process_info))
        {
            return false;
        }

        CloseHandle(process_info.hThread);
        const DWORD wait_result = WaitForSingleObject(process_info.hProcess, ENCODER_PROBE_TIMEOUT_MS);
        if (wait_result == WAIT_TIMEOUT)
        {
            TerminateProcess(process_info.hProcess, ERROR_TIMEOUT);
        }

        DWORD exit_code = ERROR_GEN_FAILURE;
        const bool supported =
            wait_result == WAIT_OBJECT_0 &&
            GetExitCodeProcess(process_info.hProcess, &exit_code) &&
            SUCCEEDED(static_cast<HRESULT>(exit_code));
        CloseHandle(process_info.hProcess);
        return supported;
    }

    int CacheEncoderAvailability()
    {
        std::wstring worker_path(MAX_PATH, L'\0');
        for (;;)
        {
            const DWORD length =
                GetModuleFileNameW(nullptr, worker_path.data(), static_cast<DWORD>(worker_path.size()));
            if (length == 0)
            {
                return static_cast<int>(HRESULT_FROM_WIN32(GetLastError()));
            }
            if (length < worker_path.size() - 1)
            {
                worker_path.resize(length);
                break;
            }
            worker_path.resize(worker_path.size() * 2);
        }

        HKEY key = nullptr;
        const LSTATUS create_result = RegCreateKeyExW(
            HKEY_CURRENT_USER,
            winrt::PowerToys::FileConverter::Constants::RegistryPath,
            0,
            nullptr,
            0,
            KEY_SET_VALUE,
            nullptr,
            &key,
            nullptr);
        if (create_result != ERROR_SUCCESS)
        {
            return static_cast<int>(HRESULT_FROM_WIN32(create_result));
        }

        for (int format = static_cast<int>(file_converter::ImageFormat::Png);
             format <= static_cast<int>(file_converter::ImageFormat::Webp);
             ++format)
        {
            const DWORD available =
                ProbeFormatInChildProcess(worker_path, static_cast<file_converter::ImageFormat>(format)) ? 1 : 0;
            const std::wstring value_name =
                std::wstring(winrt::PowerToys::FileConverter::Constants::RegistryEncoderAvailabilityPrefix) +
                std::to_wstring(format);
            RegSetValueExW(
                key,
                value_name.c_str(),
                0,
                REG_DWORD,
                reinterpret_cast<const BYTE*>(&available),
                sizeof(available));
        }
        RegCloseKey(key);
        return 0;
    }
}

int wmain(int argc, wchar_t* argv[])
{
    if (argc == 2 && wcscmp(argv[1], L"--probe-cache-all") == 0)
    {
        return CacheEncoderAvailability();
    }

    if (argc != 3)
    {
        return static_cast<int>(E_INVALIDARG);
    }

    const bool probe_only = wcscmp(argv[1], L"--probe") == 0;
    const auto format = ParseFormat(argv[2]);
    if (!format)
    {
        return static_cast<int>(E_INVALIDARG);
    }

    try
    {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        if (probe_only)
        {
            return static_cast<int>(file_converter::IsOutputFormatSupported(*format).hr);
        }

        LoggerHelpers::init_logger(L"FileConverter", L"Worker", "fileconverter");
        const std::filesystem::path input_path(argv[1]);
        std::error_code ec;
        if (input_path.empty())
        {
            return static_cast<int>(HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND));
        }
        if (!std::filesystem::is_regular_file(input_path, ec))
        {
            return static_cast<int>(ec ? HRESULT_FROM_WIN32(ec.value()) : HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND));
        }

        std::filesystem::path output_path;
        const auto output_extension = ExtensionForFormat(*format);
        for (unsigned int suffix = 0;; ++suffix)
        {
            output_path = input_path.parent_path() / input_path.stem();
            output_path += suffix == 0 ? L"_converted" : L"_converted_" + std::to_wstring(suffix);
            output_path += output_extension;
            if (!std::filesystem::exists(output_path, ec))
            {
                if (ec)
                {
                    return static_cast<int>(HRESULT_FROM_WIN32(ec.value()));
                }
                break;
            }
            ec.clear();
        }

        const auto result = file_converter::ConvertImageFile(input_path.wstring(), output_path.wstring(), *format);
        if (!result.succeeded())
        {
            Logger::error(L"Conversion failed for '{}': {}", input_path.wstring(), result.error_message);
            Logger::flush();
        }
        return static_cast<int>(result.hr);
    }
    catch (const winrt::hresult_error& error)
    {
        return static_cast<int>(error.code());
    }
    catch (...)
    {
        return static_cast<int>(E_FAIL);
    }
}
