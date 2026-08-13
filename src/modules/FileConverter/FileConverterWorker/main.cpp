#include "pch.h"

#include "../FileConverterLib/FileConversionEngine.h"
#include <common/logger/logger.h>
#include <common/utils/logger_helper.h>

namespace
{
    constexpr auto ABANDONED_TEMP_AGE = std::chrono::minutes(5);

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

    void RemoveAbandonedTempFiles(
        const std::filesystem::path& input_path,
        const std::wstring& output_extension)
    {
        const std::wstring output_prefix = input_path.stem().wstring() + L"_converted";
        const std::wstring temp_marker = output_extension + L".tmp-";
        const auto cutoff = std::filesystem::file_time_type::clock::now() - ABANDONED_TEMP_AGE;

        std::error_code ec;
        for (std::filesystem::directory_iterator iterator(input_path.parent_path(), ec), end;
             !ec && iterator != end;
             iterator.increment(ec))
        {
            const auto& candidate = iterator->path();
            const std::wstring filename = candidate.filename().wstring();
            if (!filename.starts_with(output_prefix) ||
                filename.find(temp_marker) == std::wstring::npos)
            {
                continue;
            }

            const auto write_time = iterator->last_write_time(ec);
            if (ec)
            {
                ec.clear();
                continue;
            }

            if (write_time <= cutoff)
            {
                std::filesystem::remove(candidate, ec);
                ec.clear();
            }
        }
    }
}

int wmain(int argc, wchar_t* argv[])
{
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
        RemoveAbandonedTempFiles(input_path, output_extension);
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
