#include "pch.h"
#include <common/SettingsAPI/FileWatcher.h>
#include <common/utils/atomic_file.h>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <iterator>
#include <mutex>
#include <string>
#include <system_error>
#include <vector>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace UnitTestsCommonLib
{
    namespace
    {
        class WatcherTestDirectory
        {
        public:
            WatcherTestDirectory()
            {
                GUID id{};
                Assert::IsTrue(SUCCEEDED(CoCreateGuid(&id)));
                wchar_t idString[39]{};
                Assert::IsTrue(StringFromGUID2(id, idString, ARRAYSIZE(idString)) != 0);
                m_path = std::filesystem::temp_directory_path() / (std::wstring(L"PowerToys.FileWatcher.Tests.") + idString);
                Assert::IsTrue(std::filesystem::create_directory(m_path));
            }

            ~WatcherTestDirectory()
            {
                std::error_code error;
                std::filesystem::remove_all(m_path, error);
            }

            WatcherTestDirectory(const WatcherTestDirectory&) = delete;
            WatcherTestDirectory& operator=(const WatcherTestDirectory&) = delete;

            const std::filesystem::path& path() const { return m_path; }

        private:
            std::filesystem::path m_path;
        };

        class WatchedFileCache
        {
        public:
            void reload(const std::filesystem::path& path)
            {
                std::ifstream stream(path, std::ios::binary);
                const auto readError = stream ? ERROR_SUCCESS : GetLastError();
                {
                    std::lock_guard lock(m_mutex);
                    ++m_reloadAttempts;
                    m_lastReadError = readError;
                }
                if (!stream)
                {
                    return;
                }

                const std::string contents{ std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>() };
                {
                    std::lock_guard lock(m_mutex);
                    m_contents = contents;
                }
                m_changed.notify_all();
            }

            bool waitFor(const std::string& expected)
            {
                std::unique_lock lock(m_mutex);
                const bool updated = m_changed.wait_for(lock, std::chrono::seconds(5), [&]() { return m_contents == expected; });
                if (!updated)
                {
                    const auto message = L"Cache reload attempts=" + std::to_wstring(m_reloadAttempts) + L", last read error=" + std::to_wstring(m_lastReadError) + L"\n";
                    Microsoft::VisualStudio::CppUnitTestFramework::Logger::WriteMessage(message.c_str());
                }
                return updated;
            }

        private:
            std::mutex m_mutex;
            std::condition_variable m_changed;
            std::string m_contents;
            unsigned int m_reloadAttempts = 0;
            DWORD m_lastReadError = ERROR_SUCCESS;
        };

        wil::unique_hfile createBlockedReplacement(const std::filesystem::path& directory,
                                                   const std::filesystem::path& destination,
                                                   const std::string& contents,
                                                   bool preserveTimestamp = false)
        {
            const auto staged = directory / L"settings.tmp";
            {
                std::ofstream stream(staged, std::ios::binary);
                stream << contents;
            }

            if (preserveTimestamp)
            {
                WIN32_FILE_ATTRIBUTE_DATA attributes{};
                Assert::IsTrue(GetFileAttributesExW(destination.c_str(), GetFileExInfoStandard, &attributes) != FALSE);
                wil::unique_hfile stagedFile(CreateFileW(staged.c_str(), FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr));
                Assert::IsTrue(static_cast<bool>(stagedFile));
                Assert::IsTrue(SetFileTime(stagedFile.get(), nullptr, nullptr, &attributes.ftLastWriteTime) != FALSE);
            }

            wil::unique_hfile blocker(CreateFileW(staged.c_str(), GENERIC_READ | DELETE, FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr));
            Assert::IsTrue(static_cast<bool>(blocker));

            const auto destinationName = destination.wstring();
            const auto nameLength = destinationName.size() * sizeof(wchar_t);
            std::vector<std::byte> renameBuffer(sizeof(FILE_RENAME_INFO) + nameLength);
            auto rename = reinterpret_cast<FILE_RENAME_INFO*>(renameBuffer.data());
            rename->Flags = FILE_RENAME_FLAG_REPLACE_IF_EXISTS | FILE_RENAME_FLAG_POSIX_SEMANTICS;
            rename->RootDirectory = nullptr;
            rename->FileNameLength = static_cast<DWORD>(nameLength);
            std::memcpy(rename->FileName, destinationName.data(), nameLength);
            Assert::IsTrue(SetFileInformationByHandle(blocker.get(), FileRenameInfoEx, rename, static_cast<DWORD>(renameBuffer.size())) != FALSE);
            return blocker;
        }

        bool waitForCallbackCount(const std::atomic<unsigned int>& callbackCount, unsigned int expected)
        {
            const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(6);
            while (std::chrono::steady_clock::now() < deadline)
            {
                if (callbackCount.load() >= expected)
                {
                    return true;
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
            }
            return callbackCount.load() >= expected;
        }

    }

    TEST_CLASS (FileWatcherTests)
    {
    public:
        TEST_METHOD (FirstOrdinaryWriteRefreshesCache)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"SeTtInGs.json";
            const std::string contents = "{\"enabled\":false}";
            Assert::AreEqual(static_cast<DWORD>(ERROR_SUCCESS), atomic_file::write(destination.wstring(), contents));

            WIN32_FILE_ATTRIBUTE_DATA attributes{};
            Assert::IsTrue(GetFileAttributesExW(destination.c_str(), GetFileExInfoStandard, &attributes) != FALSE);
            ULARGE_INTEGER nextWriteTime{};
            nextWriteTime.HighPart = attributes.ftLastWriteTime.dwHighDateTime;
            nextWriteTime.LowPart = attributes.ftLastWriteTime.dwLowDateTime;
            nextWriteTime.QuadPart += 10000000; // One second in FILETIME units.
            const FILETIME lastWriteTime{ nextWriteTime.LowPart, nextWriteTime.HighPart };

            WatchedFileCache cache;
            FileWatcher watcher(destination.wstring(), [&]() { cache.reload(destination); });
            {
                // Change only metadata so the first write notification cannot race
                // a partially written file or be hidden by a later content write.
                wil::unique_hfile file(CreateFileW(destination.c_str(), FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr));
                Assert::IsTrue(static_cast<bool>(file));
                Assert::IsTrue(SetFileTime(file.get(), nullptr, nullptr, &lastWriteTime) != FALSE);
            }

            Assert::IsTrue(cache.waitFor(contents), L"The first ordinary write did not refresh the cache");
        }

        TEST_METHOD (AtomicCreationRefreshesCache)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            WatchedFileCache cache;
            FileWatcher watcher(destination.wstring(), [&]() { cache.reload(destination); }, true);

            const std::string contents = "{\"enabled\":false}";
            Assert::AreEqual(static_cast<DWORD>(ERROR_SUCCESS), atomic_file::write(destination.wstring(), contents));
            Assert::IsTrue(cache.waitFor(contents), L"The first atomic creation did not refresh the cache");
        }

        TEST_METHOD (AtomicCreationRetriesAfterReadSharingViolation)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            WatchedFileCache cache;
            FileWatcher watcher(destination.wstring(), [&]() { cache.reload(destination); }, true);

            auto blocker = createBlockedReplacement(directory.path(), destination, "{\"enabled\":false}");
            std::this_thread::sleep_for(std::chrono::milliseconds(1700));
            blocker.reset();

            Assert::IsTrue(cache.waitFor("{\"enabled\":false}"), L"The replacement was not retried after its read-sharing violation cleared");
        }

        TEST_METHOD (SuccessfulNotificationSupersedesPendingRetry)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            WatchedFileCache cache;
            std::atomic<unsigned int> callbackCount = 0;
            FileWatcher watcher(destination.wstring(), [&]() {
                ++callbackCount;
                cache.reload(destination);
            }, true);

            auto blocker = createBlockedReplacement(directory.path(), destination, "{\"version\":1}");
            std::this_thread::sleep_for(std::chrono::milliseconds(700));
            blocker.reset();

            const std::string newest = "{\"version\":2}";
            Assert::AreEqual(static_cast<DWORD>(ERROR_SUCCESS), atomic_file::write(destination.wstring(), newest));
            Assert::IsTrue(cache.waitFor(newest), L"The later successful notification did not refresh the cache");
            std::this_thread::sleep_for(std::chrono::milliseconds(1500));
            Assert::AreEqual(1u, callbackCount.load(), L"The superseded retry invoked a duplicate callback");
        }

        TEST_METHOD (SameTimestampReplacementRetriesAfterReadSharingViolation)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            const std::string original = "{\"version\":1}";
            const std::string replacement = "{\"version\":2}";
            Assert::AreEqual(static_cast<DWORD>(ERROR_SUCCESS), atomic_file::write(destination.wstring(), original));

            WatchedFileCache cache;
            cache.reload(destination);
            FileWatcher watcher(destination.wstring(), [&]() { cache.reload(destination); }, true);
            auto blocker = createBlockedReplacement(directory.path(), destination, replacement, true);
            std::this_thread::sleep_for(std::chrono::milliseconds(1700));
            blocker.reset();

            Assert::IsTrue(cache.waitFor(replacement), L"A same-timestamp replacement was not refreshed after retry");
        }

        TEST_METHOD (PendingRetryIsCancelledWhenWatcherIsDisposed)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            std::atomic<unsigned int> callbackCount = 0;
            auto watcher = std::make_unique<FileWatcher>(destination.wstring(), [&]() { ++callbackCount; }, true);

            auto blocker = createBlockedReplacement(directory.path(), destination, "{\"enabled\":false}");
            std::this_thread::sleep_for(std::chrono::milliseconds(700));
            watcher.reset();
            blocker.reset();
            std::this_thread::sleep_for(std::chrono::milliseconds(1200));

            Assert::AreEqual(0u, callbackCount.load(), L"A pending retry invoked the callback after watcher disposal");
        }

        TEST_METHOD (RemovedFileCancelsPendingRetry)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            std::atomic<unsigned int> callbackCount = 0;
            FileWatcher watcher(destination.wstring(), [&]() { ++callbackCount; }, true);

            auto blocker = createBlockedReplacement(directory.path(), destination, "{\"enabled\":false}");
            std::this_thread::sleep_for(std::chrono::milliseconds(700));
            Assert::IsTrue(DeleteFileW(destination.c_str()) != FALSE);
            blocker.reset();
            std::this_thread::sleep_for(std::chrono::milliseconds(1200));

            Assert::AreEqual(0u, callbackCount.load(), L"A deleted file's pending retry invoked the callback");
        }

        TEST_METHOD (RetryCallbackExceptionDoesNotStopRetryWorker)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            std::atomic<unsigned int> callbackCount = 0;
            FileWatcher watcher(destination.wstring(), [&]() {
                ++callbackCount;
                throw std::runtime_error("callback failure");
            }, true);

            auto firstBlocker = createBlockedReplacement(directory.path(), destination, "{\"version\":1}");
            std::this_thread::sleep_for(std::chrono::milliseconds(700));
            firstBlocker.reset();
            Assert::IsTrue(waitForCallbackCount(callbackCount, 1), L"The retry callback was not invoked");

            auto secondBlocker = createBlockedReplacement(directory.path(), destination, "{\"version\":2}");
            std::this_thread::sleep_for(std::chrono::milliseconds(700));
            secondBlocker.reset();
            Assert::IsTrue(waitForCallbackCount(callbackCount, 2), L"The retry worker did not survive a callback exception");
        }

        TEST_METHOD (FirstAtomicReplacementRefreshesCache)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            const std::string original = "{\"enabled\":true}";
            Assert::AreEqual(static_cast<DWORD>(ERROR_SUCCESS), atomic_file::write(destination.wstring(), original));
            WatchedFileCache cache;
            cache.reload(destination);
            FileWatcher watcher(destination.wstring(), [&]() { cache.reload(destination); }, true);

            const std::string replacement = "{\"enabled\":false}";
            Assert::AreEqual(static_cast<DWORD>(ERROR_SUCCESS), atomic_file::write(destination.wstring(), replacement));
            Assert::IsTrue(cache.waitFor(replacement), L"The first atomic replacement did not refresh the cache");
        }

        TEST_METHOD (AtomicReplacementWithSameTimestampRefreshesCache)
        {
            WatcherTestDirectory directory;
            const auto destination = directory.path() / L"settings.json";
            const auto staged = directory.path() / L"settings.tmp";
            const std::string original = "{\"enabled\":true}";
            const std::string replacement = "{\"enabled\":false}";
            Assert::AreEqual(static_cast<DWORD>(ERROR_SUCCESS), atomic_file::write(destination.wstring(), original));
            Assert::AreEqual(static_cast<DWORD>(ERROR_SUCCESS), atomic_file::write(staged.wstring(), replacement));

            WIN32_FILE_ATTRIBUTE_DATA attributes{};
            Assert::IsTrue(GetFileAttributesExW(destination.c_str(), GetFileExInfoStandard, &attributes) != FALSE);
            {
                wil::unique_hfile file(CreateFileW(staged.c_str(), FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr));
                Assert::IsTrue(static_cast<bool>(file));
                Assert::IsTrue(SetFileTime(file.get(), nullptr, nullptr, &attributes.ftLastWriteTime) != FALSE);
            }

            WatchedFileCache cache;
            cache.reload(destination);
            FileWatcher watcher(destination.wstring(), [&]() { cache.reload(destination); }, true);

            {
                wil::unique_hfile file(CreateFileW(staged.c_str(), DELETE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr));
                Assert::IsTrue(static_cast<bool>(file));
                const auto destinationName = destination.wstring();
                const auto nameLength = destinationName.size() * sizeof(wchar_t);
                std::vector<std::byte> renameBuffer(sizeof(FILE_RENAME_INFO) + nameLength);
                auto rename = reinterpret_cast<FILE_RENAME_INFO*>(renameBuffer.data());
                rename->Flags = FILE_RENAME_FLAG_REPLACE_IF_EXISTS | FILE_RENAME_FLAG_POSIX_SEMANTICS;
                rename->RootDirectory = nullptr;
                rename->FileNameLength = static_cast<DWORD>(nameLength);
                std::memcpy(rename->FileName, destinationName.data(), nameLength);
                Assert::IsTrue(SetFileInformationByHandle(file.get(), FileRenameInfoEx, rename, static_cast<DWORD>(renameBuffer.size())) != FALSE);
            }
            Assert::IsTrue(cache.waitFor(replacement), L"A replacement with the same timestamp did not refresh the cache");
        }
    };
}
