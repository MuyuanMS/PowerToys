// Copyright (c) Microsoft Corporation. Licensed under the MIT license.
#pragma once

#include <filesystem>
#include "WorkspacesData.h"

namespace WorkspaceStore
{
    enum class UpdateResult
    {
        Updated,
        Conflict,
        Failed,
        Unverified,
    };

    // All workspace-list writers cooperate through fileName + ".lock".
    bool Write(const std::filesystem::path& fileName, const json::JsonObject& data);
    UpdateResult UpdateApplicationMetadata(const std::filesystem::path& fileName, const json::JsonObject& original, const json::JsonObject& updated);
    UpdateResult UpdateLastLaunched(const std::filesystem::path& fileName, const std::wstring& workspaceId, time_t timestamp);
}
