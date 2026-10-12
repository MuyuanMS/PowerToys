// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include "pch.h"
#include "CppUnitTest.h"

#include "../general_settings_reader.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace RunnerUnitTests
{
    TEST_CLASS (GeneralSettingsReaderTests)
    {
    public:
        TEST_METHOD_INITIALIZE(CreateSettingsFile)
        {
            winrt::init_apartment(winrt::apartment_type::single_threaded);
            wchar_t directory[MAX_PATH]{};
            const auto length = GetTempPathW(ARRAYSIZE(directory), directory);
            Assert::IsTrue(length > 0 && length < ARRAYSIZE(directory));
            wchar_t path[MAX_PATH]{};
            Assert::IsTrue(GetTempFileNameW(directory, L"pts", 0, path) != 0);
            m_settingsPath = path;
        }

        TEST_METHOD_CLEANUP(RemoveSettingsFile)
        {
            std::error_code ec;
            std::filesystem::remove(m_settingsPath, ec);
            for (unsigned int attempt = 0; attempt < 100; ++attempt)
            {
                std::filesystem::path backup_path{ m_settingsPath };
                backup_path += L".corrupt";
                if (attempt > 0)
                {
                    backup_path += L"." + std::to_wstring(attempt);
                }
                std::filesystem::remove(backup_path, ec);
            }
            winrt::uninit_apartment();
        }

        TEST_METHOD (MissingFileAllowsFirstRunSettings)
        {
            Assert::IsTrue(std::filesystem::remove(m_settingsPath));

            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);

            Assert::IsTrue(loaded.has_value());
            Assert::AreEqual(0U, loaded->Size());
            Assert::IsFalse(std::filesystem::exists(m_settingsPath));
        }

        TEST_METHOD (ValidEmptyObjectIsNotAReadFailure)
        {
            WriteSettings("{}");

            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);

            Assert::IsTrue(loaded.has_value());
            Assert::AreEqual(0U, loaded->Size());
        }

        TEST_METHOD (ExistingEmptyFileIsBackedUpAndDoesNotAllowSaving)
        {
            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);

            Assert::IsFalse(loaded.has_value());
            Assert::AreEqual(0ULL, static_cast<unsigned long long>(std::filesystem::file_size(m_settingsPath)));
            Assert::IsTrue(std::filesystem::exists(GetBackupPath()));
            Assert::AreEqual(0ULL, static_cast<unsigned long long>(std::filesystem::file_size(GetBackupPath())));
        }

        TEST_METHOD (WhitespaceOnlyFileIsBackedUpAndDoesNotAllowSaving)
        {
            WriteSettings(" \r\n\t");

            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);

            Assert::IsFalse(loaded.has_value());
            Assert::IsTrue(ReadSettings(m_settingsPath) == ReadSettings(GetBackupPath()));
        }

        TEST_METHOD (EmptyFileCanBeReadAfterWriteCompletes)
        {
            const auto empty = general_settings::load_from_file_checked(m_settingsPath);
            Assert::IsFalse(empty.has_value());
            WriteSettings(R"({"enabled":{"File Locksmith":false}})");

            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);

            Assert::IsTrue(loaded.has_value());
            Assert::IsFalse(loaded->GetNamedObject(L"enabled").GetNamedBoolean(L"File Locksmith"));
        }

        TEST_METHOD (PartialJsonIsBackedUpAndDoesNotAllowSaving)
        {
            const std::string partial = R"({"enabled":{"File Locksmith":)";
            WriteSettings(partial);

            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);

            Assert::IsFalse(loaded.has_value());
            Assert::IsTrue(partial == ReadSettings());
            Assert::IsTrue(partial == ReadSettings(GetBackupPath()));
        }

        TEST_METHOD (ExistingBackupIsNotOverwritten)
        {
            const std::string partial = R"({"enabled":{"File Locksmith":)";
            const std::string previous_backup = "previous recovery data";
            WriteSettings(partial);
            WriteFile(GetBackupPath(), previous_backup);

            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);

            Assert::IsFalse(loaded.has_value());
            Assert::IsTrue(previous_backup == ReadSettings(GetBackupPath()));
            Assert::IsTrue(partial == ReadSettings(GetBackupPath(1)));
        }

        TEST_METHOD (ValidFilePreservesAllModulePreferences)
        {
            WriteSettings(R"({"enabled":{"FancyZones":true,"File Locksmith":false}})");

            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);

            Assert::IsTrue(loaded.has_value());
            const auto enabled = loaded->GetNamedObject(L"enabled");
            Assert::AreEqual(2U, enabled.Size());
            Assert::IsTrue(enabled.GetNamedBoolean(L"FancyZones"));
            Assert::IsFalse(enabled.GetNamedBoolean(L"File Locksmith"));
        }

        TEST_METHOD (LockedFileReadFailsAndSucceedsAfterUnlock)
        {
            WriteSettings(R"({"enabled":{"File Locksmith":false}})");
            {
                wil::unique_handle lock{ CreateFileW(m_settingsPath.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr) };
                Assert::IsTrue(lock.is_valid());

                Assert::IsFalse(general_settings::load_from_file_checked(m_settingsPath).has_value());
            }

            const auto loaded = general_settings::load_from_file_checked(m_settingsPath);
            Assert::IsTrue(loaded.has_value());
            Assert::IsFalse(loaded->GetNamedObject(L"enabled").GetNamedBoolean(L"File Locksmith"));
        }

    private:
        std::wstring m_settingsPath;

        void WriteSettings(const std::string& contents)
        {
            WriteFile(m_settingsPath, contents);
        }

        void WriteFile(const std::wstring& path, const std::string& contents)
        {
            std::ofstream file;
            file.exceptions(std::ios::failbit | std::ios::badbit);
            file.open(path, std::ios::binary | std::ios::trunc);
            file << contents;
            file.close();
        }

        std::string ReadSettings()
        {
            return ReadSettings(m_settingsPath);
        }

        std::string ReadSettings(const std::wstring& path)
        {
            std::ifstream file{ path, std::ios::binary };
            return { std::istreambuf_iterator<char>{ file }, std::istreambuf_iterator<char>{} };
        }

        std::wstring GetBackupPath(unsigned int attempt = 0) const
        {
            std::wstring backup_path = m_settingsPath + L".corrupt";
            if (attempt > 0)
            {
                backup_path += L"." + std::to_wstring(attempt);
            }
            return backup_path;
        }
    };
}
