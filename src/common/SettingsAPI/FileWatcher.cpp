#include "pch.h"
#include "FileWatcher.h"
#include <utils/winapi_error.h>
#include <chrono>

namespace
{
    wil::unique_hfile openForReadCallback(const std::wstring& path)
    {
        constexpr DWORD retryDelays[] = { 10, 20, 40, 80, 160 };
        for (size_t attempt = 0;; ++attempt)
        {
            // Match std::ifstream sharing. A rename notification can arrive while
            // the publisher still holds DELETE access to the replacement file.
            wil::unique_hfile file(CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr));
            if (file)
            {
                return file;
            }

            const auto error = GetLastError();
            if ((error != ERROR_SHARING_VIOLATION && error != ERROR_ACCESS_DENIED && error != ERROR_FILE_NOT_FOUND) || attempt == ARRAYSIZE(retryDelays))
            {
                return {};
            }
            Sleep(retryDelays[attempt]);
        }
    }
}

std::optional<FILETIME> FileWatcher::MyFileTime()
{
    HANDLE hFile = CreateFileW(m_path.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_DELETE | FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    std::optional<FILETIME> result;
    if (hFile != INVALID_HANDLE_VALUE)
    {
        FILETIME lastWrite;
        if (GetFileTime(hFile, nullptr, nullptr, &lastWrite))
        {
            result = lastWrite;
        }

        CloseHandle(hFile);
    }

    return result;
}

FileWatcher::FileWatcher(const std::wstring& path, std::function<void()> callback) :
    FileWatcher(path, callback, false)
{
}

FileWatcher::FileWatcher(const std::wstring& path, std::function<void()> callback, bool watchFileReplacement) :
    m_path(path),
    m_callback(callback),
    m_watchFileReplacement(watchFileReplacement)
{
    if (watchFileReplacement)
    {
        m_retryThread = std::jthread([this](std::stop_token stop) {
            RetryWorker(stop);
        });
    }

    std::filesystem::path fsPath(path);
    m_file_name = fsPath.filename();
    std::transform(m_file_name.begin(), m_file_name.end(), m_file_name.begin(), ::towlower);
    auto events = wil::FolderChangeEvents::LastWriteTime;
    if (watchFileReplacement)
    {
        events = events | wil::FolderChangeEvents::FileName;
        std::lock_guard stateLock(m_stateMutex);
        m_lastWrite = MyFileTime();
    }

    m_folder_change_reader = wil::make_folder_change_reader_nothrow(
        fsPath.parent_path().c_str(),
        false,
        events,
        [this](wil::FolderChangeEvent event, PCWSTR fileName) {
            HandleFileChange(event, fileName);
        });

    if (!m_folder_change_reader)
    {
        Logger::error(L"Failed to start folder change reader for path {}. {}", path, get_last_error_or_default(GetLastError()));
    }
}

void FileWatcher::HandleFileChange(wil::FolderChangeEvent event, PCWSTR fileName)
{
    const bool changesLost = m_watchFileReplacement && event == wil::FolderChangeEvent::ChangesLost;
    const bool removed = event == wil::FolderChangeEvent::Removed || event == wil::FolderChangeEvent::RenameOldName;
    const bool namelessRemoval = m_watchFileReplacement && removed && !fileName;
    if (!changesLost && !namelessRemoval)
    {
        if (!fileName)
        {
            return;
        }

        std::wstring lowerFileName(fileName);
        std::transform(lowerFileName.begin(), lowerFileName.end(), lowerFileName.begin(), ::towlower);
        if (m_file_name.compare(lowerFileName) != 0)
        {
            return;
        }
    }

    std::lock_guard callbackLock(m_callbackMutex);

    std::uint64_t generation = 0;
    bool mustInvoke = false;
    if (m_watchFileReplacement)
    {
        std::lock_guard stateLock(m_stateMutex);
        if (m_stopping)
        {
            return;
        }

        generation = ++m_eventGeneration;
        if (removed)
        {
            m_retryPending = false;
            m_retryMustInvoke = false;
            m_retryCondition.notify_all();
            return;
        }

        mustInvoke = !m_lastWrite.has_value() || changesLost ||
                     event == wil::FolderChangeEvent::Added ||
                     event == wil::FolderChangeEvent::RenameNewName;
    }

    std::optional<FILETIME> lastWrite;
    wil::unique_hfile readGuard;
    if (m_watchFileReplacement)
    {
        readGuard = openForReadCallback(m_path);
        FILETIME time{};
        if (!readGuard || !GetFileTime(readGuard.get(), nullptr, nullptr, &time))
        {
            {
                std::lock_guard stateLock(m_stateMutex);
                if (!m_stopping && generation == m_eventGeneration)
                {
                    m_retryPending = true;
                    m_retryGeneration = generation;
                    m_retryMustInvoke = m_retryMustInvoke || mustInvoke;
                }
            }
            m_retryCondition.notify_one();
            return;
        }
        lastWrite = time;
    }
    else
    {
        lastWrite = MyFileTime();
    }

    bool invokeCallback = false;
    bool notifyRetryWorker = false;
    if (lastWrite.has_value())
    {
        std::lock_guard stateLock(m_stateMutex);
        if (m_stopping || (m_watchFileReplacement && generation != m_eventGeneration))
        {
            return;
        }

        invokeCallback = (m_watchFileReplacement &&
                          (!m_lastWrite.has_value() || changesLost ||
                           event == wil::FolderChangeEvent::Added ||
                           event == wil::FolderChangeEvent::RenameNewName)) ||
                         (!m_lastWrite.has_value() ||
                          m_lastWrite->dwHighDateTime != lastWrite->dwHighDateTime ||
                          m_lastWrite->dwLowDateTime != lastWrite->dwLowDateTime);
        m_lastWrite = lastWrite;

        if (m_watchFileReplacement && m_retryPending)
        {
            if (invokeCallback || !m_retryMustInvoke)
            {
                m_retryPending = false;
                m_retryMustInvoke = false;
                notifyRetryWorker = true;
            }
            else
            {
                m_retryGeneration = generation;
                notifyRetryWorker = true;
            }
        }
    }

    if (notifyRetryWorker)
    {
        m_retryCondition.notify_all();
    }

    if (invokeCallback)
    {
        InvokeCallback(false);
    }
}

FileWatcher::~FileWatcher()
{
    m_retryThread.request_stop();
    {
        std::lock_guard stateLock(m_stateMutex);
        m_stopping = true;
        ++m_eventGeneration;
        m_retryPending = false;
        m_retryMustInvoke = false;
    }
    m_retryCondition.notify_all();
    m_folder_change_reader.reset();
    {
        std::lock_guard callbackLock(m_callbackMutex);
    }
    if (m_retryThread.joinable())
    {
        m_retryThread.join();
    }
}

void FileWatcher::InvokeCallback(bool catchExceptions)
{
    if (!catchExceptions)
    {
        m_callback();
        return;
    }

    try
    {
        m_callback();
    }
    catch (...)
    {
        Logger::error(L"FileWatcher replacement callback failed.");
    }
}

void FileWatcher::RetryWorker(std::stop_token stop)
{
    const HRESULT apartmentResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(apartmentResult))
    {
        Logger::error(L"Failed to initialize the FileWatcher retry thread apartment. {}", apartmentResult);
        return;
    }

    while (!stop.stop_requested())
    {
        std::unique_lock stateLock(m_stateMutex);
        if (!m_retryCondition.wait(stateLock, stop, [this]() { return m_stopping || m_retryPending; }) ||
            m_stopping || stop.stop_requested())
        {
            break;
        }

        const auto generation = m_retryGeneration;
        if (m_retryCondition.wait_for(stateLock, stop, std::chrono::seconds(1), [this, generation]() {
                return m_stopping || !m_retryPending || m_retryGeneration != generation;
            }))
        {
            continue;
        }
        if (stop.stop_requested())
        {
            break;
        }

        stateLock.unlock();
        auto readGuard = openForReadCallback(m_path);
        FILETIME time{};
        const bool readable = readGuard && GetFileTime(readGuard.get(), nullptr, nullptr, &time);
        if (!readable)
        {
            continue;
        }

        std::lock_guard callbackLock(m_callbackMutex);
        stateLock.lock();
        if (m_stopping || stop.stop_requested() || !m_retryPending ||
            m_retryGeneration != generation || m_eventGeneration != generation)
        {
            continue;
        }

        const bool invokeCallback = m_retryMustInvoke || !m_lastWrite.has_value() ||
                                   m_lastWrite->dwHighDateTime != time.dwHighDateTime ||
                                   m_lastWrite->dwLowDateTime != time.dwLowDateTime;
        m_lastWrite = time;
        m_retryPending = false;
        m_retryMustInvoke = false;
        stateLock.unlock();

        if (invokeCallback)
        {
            InvokeCallback(true);
        }
    }

    CoUninitialize();
}
