#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>

#include <condition_variable>
#include <cstdint>
#include <functional>
#include <mutex>
#include <optional>
#include <string>
#include <thread>

#include <wil/resource.h>
#include <wil/filesystem.h>

class FileWatcher
{
    std::wstring m_path;
    std::wstring m_file_name;
    std::optional<FILETIME> m_lastWrite;
    std::function<void()> m_callback;
    wil::unique_folder_change_reader_nothrow m_folder_change_reader;
    std::mutex m_callbackMutex;
    std::mutex m_stateMutex;
    std::condition_variable_any m_retryCondition;
    std::jthread m_retryThread;
    std::uint64_t m_eventGeneration = 0;
    std::uint64_t m_retryGeneration = 0;
    bool m_retryPending = false;
    bool m_retryMustInvoke = false;
    bool m_stopping = false;
    bool m_watchFileReplacement = false;

    void HandleFileChange(wil::FolderChangeEvent event, PCWSTR fileName);
    void InvokeCallback(bool catchExceptions);
    std::optional<FILETIME> MyFileTime();
    void RetryWorker(std::stop_token stop);
public:
    FileWatcher(const std::wstring& path, std::function<void()> callback);
    // Enable replacement notifications for files published with an atomic rename.
    // The callback reads the path separately; the guard handle is not a snapshot of
    // that read and is released after reading file metadata, before the callback runs.
    // Replacement callbacks must not destroy this watcher or its owner.
    FileWatcher(const std::wstring& path, std::function<void()> callback, bool watchFileReplacement);
    ~FileWatcher();
};
