#include "pch.h"
#include "SvgThumbnailProvider.h"

#include <filesystem>
#include <fstream>
#include <shellapi.h>
#include <Shlwapi.h>
#include <string>
#include <wincodec.h>

#include <wil/com.h>

#include <common/interop/shared_constants.h>
#include <common/logger/logger.h>
#include <common/SettingsAPI/settings_helpers.h>
#include <common/utils/process_path.h>

extern HINSTANCE g_hInst;
extern long g_cDllRef;

static HBITMAP LoadPngAsPremultipliedDib(const std::wstring& filePath)
{
    wil::com_ptr<IWICImagingFactory> factory;
    if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(factory.put()))))
    {
        return nullptr;
    }

    wil::com_ptr<IWICBitmapDecoder> decoder;
    if (FAILED(factory->CreateDecoderFromFilename(filePath.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnLoad, decoder.put())))
    {
        return nullptr;
    }

    wil::com_ptr<IWICBitmapFrameDecode> frame;
    if (FAILED(decoder->GetFrame(0, frame.put())))
    {
        return nullptr;
    }

    wil::com_ptr<IWICFormatConverter> converter;
    if (FAILED(factory->CreateFormatConverter(converter.put())) ||
        FAILED(converter->Initialize(frame.get(), GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom)))
    {
        return nullptr;
    }

    UINT width = 0;
    UINT height = 0;
    if (FAILED(converter->GetSize(&width, &height)) || width == 0 || height == 0 || width > LONG_MAX || height > LONG_MAX)
    {
        return nullptr;
    }

    BITMAPINFO bitmapInfo{};
    bitmapInfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bitmapInfo.bmiHeader.biWidth = static_cast<LONG>(width);
    bitmapInfo.bmiHeader.biHeight = -static_cast<LONG>(height);
    bitmapInfo.bmiHeader.biPlanes = 1;
    bitmapInfo.bmiHeader.biBitCount = 32;
    bitmapInfo.bmiHeader.biCompression = BI_RGB;

    void* bits = nullptr;
    HBITMAP bitmap = CreateDIBSection(nullptr, &bitmapInfo, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (bitmap == nullptr || bits == nullptr)
    {
        if (bitmap != nullptr)
        {
            DeleteObject(bitmap);
        }

        return nullptr;
    }

    if (FAILED(converter->CopyPixels(nullptr, width * 4, width * height * 4, static_cast<BYTE*>(bits))))
    {
        DeleteObject(bitmap);
        return nullptr;
    }

    return bitmap;
}

SvgThumbnailProvider::SvgThumbnailProvider() :
    m_cRef(1), m_pStream(NULL), m_process(NULL)
{
    std::filesystem::path logFilePath(PTSettingsHelper::get_local_low_folder_location());
    logFilePath.append(LogSettings::svgThumbLogPath);
    Logger::init(LogSettings::svgThumbLoggerName, logFilePath.wstring(), PTSettingsHelper::get_log_settings_file_location());

    InterlockedIncrement(&g_cDllRef);
}

SvgThumbnailProvider::~SvgThumbnailProvider()
{
    InterlockedDecrement(&g_cDllRef);
}

#pragma region IUnknown

IFACEMETHODIMP SvgThumbnailProvider::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB qit[] = {
        QITABENT(SvgThumbnailProvider, IThumbnailProvider),
        QITABENT(SvgThumbnailProvider, IInitializeWithStream),
        { 0 },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG)
SvgThumbnailProvider::AddRef()
{
    return InterlockedIncrement(&m_cRef);
}

IFACEMETHODIMP_(ULONG)
SvgThumbnailProvider::Release()
{
    ULONG cRef = InterlockedDecrement(&m_cRef);
    if (0 == cRef)
    {
        delete this;
    }
    return cRef;
}

#pragma endregion

#pragma region IInitializationWithStream

IFACEMETHODIMP SvgThumbnailProvider::Initialize(IStream* pStream, DWORD grfMode)
{
    HRESULT hr = E_INVALIDARG;
    if (pStream)
    {
        // Initialize can be called more than once, so release existing valid
        // m_pStream.
        if (m_pStream)
        {
            m_pStream->Release();
            m_pStream = NULL;
        }

        m_pStream = pStream;
        m_pStream->AddRef();
        hr = S_OK;
    }
    return hr;
}

#pragma endregion

#pragma region IThumbnailProvider

IFACEMETHODIMP SvgThumbnailProvider::GetThumbnail(UINT cx, HBITMAP* phbmp, WTS_ALPHATYPE* pdwAlpha)
{
    // Read stream into the buffer
    char buffer[4096];
    ULONG cbRead;

    Logger::trace(L"Begin");

    GUID guid;
    if (CoCreateGuid(&guid) == S_OK)
    {
        wil::unique_cotaskmem_string guidString;
        if (SUCCEEDED(StringFromCLSID(guid, &guidString)))
        {
            Logger::info(L"Read stream and save to tmp file.");

            // {CLSID} -> CLSID
            std::wstring guid = std::wstring(guidString.get()).substr(1, std::wstring(guidString.get()).size() - 2);
            std::wstring filePath = PTSettingsHelper::get_local_low_folder_location() + L"\\SvgThumbnailPreview-Temp\\";
            if (!std::filesystem::exists(filePath))
            {
                std::filesystem::create_directories(filePath);
            }

            std::wstring fileName = filePath + guid + L".svg";

            // Write data to tmp file
            std::fstream file;
            file.open(fileName, std::ios_base::out | std::ios_base::binary);

            if (!file.is_open())
            {
                return 0;
            }

            while (true)
            {
                auto result = m_pStream->Read(buffer, 4096, &cbRead);

                file.write(buffer, cbRead);
                if (result == S_FALSE)
                {
                    break;
                }
            }
            file.close();

            m_pStream->Release();
            m_pStream = NULL;

            try
            {
                Logger::info(L"Start SvgThumbnailProvider.exe");

                STARTUPINFO info = { sizeof(info) };
                std::wstring cmdLine{ L"\"" + fileName + L"\"" };
                cmdLine += L" ";
                cmdLine += std::to_wstring(cx);

                std::wstring appPath = get_module_folderpath(g_hInst) + L"\\PowerToys.SvgThumbnailProvider.exe";

                SHELLEXECUTEINFO sei{ sizeof(sei) };
                sei.fMask = { SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI };
                sei.lpFile = appPath.c_str();
                sei.lpParameters = cmdLine.c_str();
                sei.nShow = SW_SHOWDEFAULT;
                ShellExecuteEx(&sei);
                m_process = sei.hProcess;
                WaitForSingleObject(m_process, INFINITE);
                std::filesystem::remove(fileName);

                std::wstring fileNamePng = filePath + guid + L".png";

                if (std::filesystem::exists(fileNamePng))
                {
                    *phbmp = LoadPngAsPremultipliedDib(fileNamePng);
                    if (*phbmp != nullptr)
                    {
                        *pdwAlpha = WTS_ALPHATYPE::WTSAT_ARGB;
                    }

                    std::filesystem::remove(fileNamePng);
                }
                else
                {
                    Logger::info(L"Bmp file not generated.");
                    return E_FAIL;
                }
            }
            catch (std::exception& e)
            {
                std::wstring errorMessage = std::wstring{ winrt::to_hstring(e.what()) };
                Logger::error(L"Failed to start SvgThumbnailProvider.exe. Error: {}", errorMessage);
            }
        }
    }

    // ensure releasing the stream (not all if branches contain it)
    if (m_pStream)
    {
        m_pStream->Release();
        m_pStream = NULL;
    }

    return S_OK;
}

#pragma endregion

#pragma region Helper Functions

#pragma endregion
