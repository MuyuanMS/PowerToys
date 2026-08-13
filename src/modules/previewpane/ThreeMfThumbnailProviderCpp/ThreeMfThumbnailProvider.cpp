#include "pch.h"
#include "ThreeMfThumbnailProvider.h"

#include <filesystem>
#include <fstream>
#include <shellapi.h>
#include <Shlwapi.h>
#include <string>

#include <wil/com.h>

#include <common/interop/shared_constants.h>
#include <common/logger/logger.h>
#include <common/SettingsAPI/settings_helpers.h>
#include <common/utils/process_path.h>

extern HINSTANCE g_hInst;
extern long g_cDllRef;

ThreeMfThumbnailProvider::ThreeMfThumbnailProvider() :
    m_cRef(1), m_pStream(NULL), m_process(NULL)
{
    std::filesystem::path logFilePath(PTSettingsHelper::get_local_low_folder_location());
    logFilePath.append(LogSettings::threeMfThumbLogPath);
    Logger::init(LogSettings::threeMfThumbLoggerName, logFilePath.wstring(), PTSettingsHelper::get_log_settings_file_location());

    InterlockedIncrement(&g_cDllRef);
}

ThreeMfThumbnailProvider::~ThreeMfThumbnailProvider()
{
    // Release the stream retained in Initialize in case the object is destroyed
    // without GetThumbnail ever being called.
    if (m_pStream)
    {
        m_pStream->Release();
        m_pStream = NULL;
    }

    if (m_process)
    {
        CloseHandle(m_process);
        m_process = NULL;
    }

    InterlockedDecrement(&g_cDllRef);
}

#pragma region IUnknown

IFACEMETHODIMP ThreeMfThumbnailProvider::QueryInterface(REFIID riid, void** ppv)
{
    static const QITAB qit[] = {
        QITABENT(ThreeMfThumbnailProvider, IThumbnailProvider),
        QITABENT(ThreeMfThumbnailProvider, IInitializeWithStream),
        { 0 },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG)
ThreeMfThumbnailProvider::AddRef()
{
    return InterlockedIncrement(&m_cRef);
}

IFACEMETHODIMP_(ULONG)
ThreeMfThumbnailProvider::Release()
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

IFACEMETHODIMP ThreeMfThumbnailProvider::Initialize(IStream* pStream, DWORD grfMode)
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

IFACEMETHODIMP ThreeMfThumbnailProvider::GetThumbnail(UINT cx, HBITMAP* phbmp, WTS_ALPHATYPE* pdwAlpha)
{
    if (!phbmp || !pdwAlpha || !m_pStream || cx == 0)
    {
        return E_INVALIDARG;
    }

    *phbmp = NULL;
    *pdwAlpha = WTSAT_UNKNOWN;

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
            Logger::trace(L"Read stream and save to tmp file.");
            
            // {CLSID} -> CLSID
            std::wstring guid = std::wstring(guidString.get()).substr(1, std::wstring(guidString.get()).size() - 2);
            std::wstring filePath = PTSettingsHelper::get_local_low_folder_location() + L"\\ThreeMfThumbnail-Temp\\";

            // Use the error_code overloads: these run outside the try/catch below, so a throwing
            // filesystem_error (e.g. inaccessible LocalLow) would otherwise escape the COM boundary
            // into Explorer and leak the retained stream. Fail cleanly with an HRESULT instead.
            std::error_code createEc;
            if (!std::filesystem::exists(filePath, createEc))
            {
                std::filesystem::create_directories(filePath, createEc);
            }

            if (createEc)
            {
                Logger::error(L"Failed to create temporary directory for thumbnail generation.");
                m_pStream->Release();
                m_pStream = NULL;
                return E_FAIL;
            }

            std::wstring fileName = filePath + guid + L".3mf";

            // Write data to tmp file
            std::fstream file;
            file.open(fileName, std::ios_base::out | std::ios_base::binary);

            if (!file.is_open())
            {
                Logger::error(L"Failed to create temporary file for thumbnail generation.");
                m_pStream->Release();
                m_pStream = NULL;
                return E_FAIL;
            }

            // Cap the amount copied from the (untrusted) source stream so browsing a very large or
            // attacker-supplied .3mf cannot fill the disk or block this loop indefinitely.
            constexpr unsigned long long MaxPackageBytes = 256ULL * 1024 * 1024; // 256 MB
            unsigned long long totalCopied = 0;

            while (true)
            {
                HRESULT result = m_pStream->Read(buffer, sizeof(buffer), &cbRead);
                if (FAILED(result))
                {
                    // On a failed read cbRead is not valid and the loop would otherwise
                    // spin writing stale data. Clean up and propagate the failure.
                    Logger::error(L"Failed to read from source stream.");
                    file.close();
                    std::error_code removeEc;
                    std::filesystem::remove(fileName, removeEc);
                    m_pStream->Release();
                    m_pStream = NULL;
                    return result;
                }

                if (cbRead > 0)
                {
                    totalCopied += cbRead;
                    if (totalCopied > MaxPackageBytes)
                    {
                        Logger::error(L"3MF package exceeds the maximum supported size; aborting.");
                        file.close();
                        std::error_code removeEc;
                        std::filesystem::remove(fileName, removeEc);
                        m_pStream->Release();
                        m_pStream = NULL;
                        return E_FAIL;
                    }

                    file.write(buffer, cbRead);
                    if (!file.good())
                    {
                        Logger::error(L"Failed to write the temporary 3MF package.");
                        file.close();
                        std::error_code removeEc;
                        std::filesystem::remove(fileName, removeEc);
                        m_pStream->Release();
                        m_pStream = NULL;
                        return E_FAIL;
                    }
                }

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
                Logger::trace(L"Start ThreeMfThumbnailProvider.exe");
                
                STARTUPINFO info = { sizeof(info) };
                std::wstring cmdLine{ L"\"" + fileName + L"\"" };
                cmdLine += L" ";
                cmdLine += std::to_wstring(cx);

                std::wstring appPath = get_module_folderpath(g_hInst) + L"\\PowerToys.ThreeMfThumbnailProvider.exe";

                // The worker writes its output next to the input using the same guid; compute it up
                // front so every failure path can also remove a partially written BMP.
                std::wstring fileNameBmp = filePath + guid + L".bmp";

                SHELLEXECUTEINFO sei{ sizeof(sei) };
                sei.fMask = { SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI };
                sei.lpFile = appPath.c_str();
                sei.lpParameters = cmdLine.c_str();
                sei.nShow = SW_SHOWDEFAULT;

                std::error_code ec;

                if (!ShellExecuteEx(&sei) || sei.hProcess == NULL)
                {
                    Logger::error(L"Failed to start PowerToys.ThreeMfThumbnailProvider.exe.");
                    std::filesystem::remove(fileName, ec);
                    return E_FAIL;
                }

                wil::unique_handle processHandle{ sei.hProcess };
                m_process = processHandle.get();

                // Bound the wait so a malformed or expensive 3MF cannot block the Explorer
                // thumbnail host indefinitely, and always close the worker process handle.
                constexpr DWORD ThumbnailTimeoutMs = 30000;
                DWORD waitResult = WaitForSingleObject(m_process, ThumbnailTimeoutMs);
                if (waitResult != WAIT_OBJECT_0)
                {
                    Logger::error(L"Thumbnail generation timed out; terminating worker process.");
                    TerminateProcess(processHandle.get(), 1);
                    WaitForSingleObject(processHandle.get(), 5000);
                    processHandle.reset();
                    m_process = NULL;
                    std::filesystem::remove(fileName, ec);

                    // The worker may have already created or partially written the output BMP before
                    // being terminated; remove it too so stale files cannot accumulate in LocalLow.
                    std::filesystem::remove(fileNameBmp, ec);
                    return E_FAIL;
                }

                processHandle.reset();
                m_process = NULL;

                std::filesystem::remove(fileName, ec);

                if (std::filesystem::exists(fileNameBmp))
                {
                    HBITMAP hbmp = static_cast<HBITMAP>(LoadImage(NULL, fileNameBmp.c_str(), IMAGE_BITMAP, 0, 0, LR_LOADFROMFILE));
                    std::filesystem::remove(fileNameBmp, ec);

                    if (hbmp == NULL)
                    {
                        Logger::error(L"Failed to load generated bitmap.");
                        return E_FAIL;
                    }

                    *phbmp = hbmp;
                    *pdwAlpha = WTS_ALPHATYPE::WTSAT_ARGB;

                    // Only report success once both COM output parameters have been assigned.
                    return S_OK;
                }
                else
                {
                    Logger::warn(L"Bmp file not generated.");
                    return E_FAIL;
                }
            }
            catch (std::exception& e)
            {
                m_process = NULL;
                std::error_code ec;
                std::filesystem::remove(fileName, ec);
                std::filesystem::remove(filePath + guid + L".bmp", ec);
                std::wstring errorMessage = std::wstring{ winrt::to_hstring(e.what()) };
                Logger::error(L"Failed to start ThreeMfThumbnailProvider.exe. Error: {}", errorMessage);
            }
        }
    }

    // ensure releasing the stream (not all if branches contain it)
    if (m_pStream)
    {
        m_pStream->Release();
        m_pStream = NULL;
    }

    // Default to failure: control only reaches here through GUID/CLSID failures or the
    // exception path, none of which assigned the output parameters.
    return E_FAIL;
}

#pragma endregion

#pragma region Helper Functions

#pragma endregion
