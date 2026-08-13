#include "pch.h"

#include "../FileConverterLib/FileConversionEngine.h"

namespace
{
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
