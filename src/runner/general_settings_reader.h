// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma once

#include <array>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <optional>
#include <string>
#include <thread>

#include <winrt/base.h>

#include <common/logger/logger.h>
#include <common/utils/json.h>

namespace general_settings
{
    inline std::optional<std::filesystem::path> preserve_invalid_settings_file(
        const std::wstring& settings_path,
        const std::string& contents)
    {
        for (unsigned int attempt = 0; attempt < 100; ++attempt)
        {
            std::filesystem::path backup_path{ settings_path };
            std::wstring suffix = L".corrupt";
            if (attempt > 0)
            {
                suffix += L"." + std::to_wstring(attempt);
            }
            backup_path += suffix;

            std::error_code ec;
            const bool backup_exists = std::filesystem::exists(backup_path, ec);
            if (ec)
            {
                return std::nullopt;
            }
            if (backup_exists)
            {
                std::ifstream existing_backup{ std::filesystem::path{ backup_path }, std::ios::binary };
                if (existing_backup.is_open())
                {
                    using isbi = std::istreambuf_iterator<char>;
                    std::string saved_contents{ isbi{ existing_backup }, isbi{} };
                    if (!existing_backup.bad() && saved_contents == contents)
                    {
                        return backup_path;
                    }
                }
                continue;
            }

            std::filesystem::copy_file(settings_path, backup_path, std::filesystem::copy_options::none, ec);
            if (ec)
            {
                std::error_code exists_ec;
                if (std::filesystem::exists(backup_path, exists_ec) && !exists_ec)
                {
                    continue;
                }
                return std::nullopt;
            }

            std::ifstream backup_file{ std::filesystem::path{ backup_path }, std::ios::binary };
            if (!backup_file.is_open())
            {
                return std::nullopt;
            }

            using isbi = std::istreambuf_iterator<char>;
            std::string saved_contents{ isbi{ backup_file }, isbi{} };
            if (backup_file.bad() || saved_contents != contents)
            {
                return std::nullopt;
            }

            return backup_path;
        }

        return std::nullopt;
    }

    inline std::optional<json::JsonObject> load_from_file_checked(const std::wstring& settings_path)
    {
        std::error_code ec;
        const bool exists = std::filesystem::exists(settings_path, ec);
        if (ec)
        {
            Logger::warn("load_from_file_checked: Failed to check if settings.json exists: {}", ec.message());
            return std::nullopt;
        }

        if (!exists)
        {
            return json::JsonObject{};
        }

        std::ifstream settings_file{ std::filesystem::path{ settings_path }, std::ios::binary };
        if (!settings_file.is_open())
        {
            Logger::warn(L"load_from_file_checked: Failed to open existing settings.json.");
            return std::nullopt;
        }

        using isbi = std::istreambuf_iterator<char>;
        std::string contents{ isbi{ settings_file }, isbi{} };
        if (settings_file.bad())
        {
            Logger::warn(L"load_from_file_checked: Failed to read existing settings.json.");
            return std::nullopt;
        }

        if (contents.empty())
        {
            settings_file.close();

            constexpr std::array retry_delays{ std::chrono::milliseconds{ 25 }, std::chrono::milliseconds{ 50 }, std::chrono::milliseconds{ 100 } };
            for (const auto delay : retry_delays)
            {
                std::this_thread::sleep_for(delay);

                std::ifstream retry_file{ std::filesystem::path{ settings_path }, std::ios::binary };
                if (!retry_file.is_open())
                {
                    Logger::warn(L"load_from_file_checked: Failed to reopen existing settings.json while retrying an empty read.");
                    return std::nullopt;
                }

                contents.assign(isbi{ retry_file }, isbi{});
                if (retry_file.bad())
                {
                    Logger::warn(L"load_from_file_checked: Failed to read existing settings.json while retrying an empty read.");
                    return std::nullopt;
                }

                if (!contents.empty())
                {
                    break;
                }
            }

            if (contents.empty())
            {
                const auto backup_path = preserve_invalid_settings_file(settings_path, contents);
                if (!backup_path.has_value())
                {
                    Logger::warn(L"load_from_file_checked: Empty settings.json could not be safely preserved.");
                    return std::nullopt;
                }

                Logger::warn(
                    L"load_from_file_checked: Empty settings.json was preserved as {}; defaults will be used until settings are saved.",
                    backup_path->wstring());
                return json::JsonObject{};
            }
        }

        json::JsonObject loaded;
        if (json::JsonObject::TryParse(winrt::to_hstring(contents), loaded))
        {
            return loaded;
        }

        const auto backup_path = preserve_invalid_settings_file(settings_path, contents);
        if (!backup_path.has_value())
        {
            Logger::warn(L"load_from_file_checked: Invalid settings.json could not be safely preserved.");
            return std::nullopt;
        }

        Logger::warn(
            L"load_from_file_checked: Invalid settings.json was preserved as {}; defaults will be used until settings are saved.",
            backup_path->wstring());
        return json::JsonObject{};
    }
}
