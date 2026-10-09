// Copyright (c) Microsoft Corporation. Licensed under the MIT license.
#include "pch.h"
#include <WorkspacesLib/CliCommands.h>
#include <WorkspacesLib/WorkspaceStore.h>
#include <wil/resource.h>
#include <objbase.h>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace WorkspacesLibUnitTests
{
    namespace
    {
        struct StoreFixture
        {
            std::filesystem::path directory;
            std::filesystem::path file;

            StoreFixture()
            {
                GUID guid{};
                Assert::IsTrue(SUCCEEDED(CoCreateGuid(&guid)));
                wchar_t value[40]{};
                StringFromGUID2(guid, value, ARRAYSIZE(value));
                directory = std::filesystem::temp_directory_path() / (std::wstring(L"WorkspacesStore-") + value);
                Assert::IsTrue(std::filesystem::create_directory(directory));
                file = directory / L"workspaces.json";
            }

            ~StoreFixture()
            {
                SetFileAttributesW(file.c_str(), FILE_ATTRIBUTE_NORMAL);
                std::error_code error;
                std::filesystem::remove_all(directory, error);
            }
        };

        const std::wstring Id = L"{6CF910A2-D2E0-436D-A50E-41432A88452A}";

        json::JsonObject Data()
        {
            return json::JsonObject::Parse(LR"({"workspaces":[{"id":"{6CF910A2-D2E0-436D-A50E-41432A88452A}","name":"Edited name","last-launched-time":10,"future":{"preserve":true},"applications":[]}],"extra":42})");
        }
    }

    TEST_CLASS (WorkspaceStoreTests)
    {
    public:
        TEST_METHOD (HistoryUpdatePreservesFreshConfigurationAndUnknownFields)
        {
            StoreFixture fixture;
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, Data()));
            Assert::IsTrue(WorkspaceStore::UpdateLastLaunched(fixture.file, Id, 100) == WorkspaceStore::UpdateResult::Updated);
            const auto saved = *WorkspacesCli::ReadJson(fixture.file);
            const auto workspace = saved.GetNamedArray(L"workspaces").GetObjectAt(0);
            Assert::AreEqual(42.0, saved.GetNamedNumber(L"extra"));
            Assert::AreEqual(std::wstring(L"Edited name"), std::wstring(workspace.GetNamedString(L"name")));
            Assert::IsTrue(workspace.GetNamedObject(L"future").GetNamedBoolean(L"preserve"));
            Assert::AreEqual(100.0, workspace.GetNamedNumber(L"last-launched-time"));
        }

        TEST_METHOD (CachedWriterDoesNotOverwriteNewLaunchHistory)
        {
            StoreFixture fixture;
            auto cached = Data();
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, cached));
            Assert::IsTrue(WorkspaceStore::UpdateLastLaunched(fixture.file, Id, 100) == WorkspaceStore::UpdateResult::Updated);
            cached.GetNamedArray(L"workspaces").GetObjectAt(0).SetNamedValue(L"name", json::value(L"New editor name"));
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, cached));
            const auto item = WorkspacesCli::ReadJson(fixture.file)->GetNamedArray(L"workspaces").GetObjectAt(0);
            Assert::AreEqual(100.0, item.GetNamedNumber(L"last-launched-time"));
            Assert::AreEqual(std::wstring(L"New editor name"), std::wstring(item.GetNamedString(L"name")));
        }

        TEST_METHOD (ApplicationMetadataUpdatePreservesOtherWorkspaceChanges)
        {
            StoreFixture fixture;
            WorkspacesData::WorkspacesProject project;
            project.id = Id;
            project.name = L"Edited name";
            project.creationTime = 10;
            WorkspacesData::WorkspacesProject::Application app;
            app.id = L"app-1";
            app.path = L"old.exe";
            app.packageFullName = L"old";
            project.apps.push_back(app);
            const auto originalWorkspace = WorkspacesData::WorkspacesProjectJSON::ToJson(project);
            json::JsonArray initialWorkspaces;
            initialWorkspaces.Append(originalWorkspace);
            json::JsonObject originalList;
            originalList.SetNamedValue(L"workspaces", initialWorkspaces);
            originalList.SetNamedValue(L"extra", json::value(42));
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, originalList));

            auto updated = json::JsonObject::Parse(originalWorkspace.Stringify());
            auto updatedApp = updated.GetNamedArray(L"applications").GetObjectAt(0);
            updatedApp.SetNamedValue(L"path", json::value(L"new.exe"));
            updatedApp.SetNamedValue(L"packageFullName", json::value(L"new"));

            auto concurrent = WorkspacesCli::ReadJson(fixture.file);
            auto concurrentWorkspaces = concurrent->GetNamedArray(L"workspaces");
            concurrentWorkspaces.Append(json::JsonObject::Parse(LR"({"id":"{00000000-0000-0000-0000-000000000001}","name":"New workspace","applications":[]})"));
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, *concurrent));

            Assert::IsTrue(WorkspaceStore::UpdateApplicationMetadata(fixture.file, originalWorkspace, updated) == WorkspaceStore::UpdateResult::Updated);
            const auto savedDocument = WorkspacesCli::ReadJson(fixture.file);
            const auto saved = savedDocument->GetNamedArray(L"workspaces");
            Assert::AreEqual(2u, saved.Size());
            Assert::AreEqual(42.0, savedDocument->GetNamedNumber(L"extra"));
            Assert::AreEqual(std::wstring(L"New workspace"), std::wstring(saved.GetObjectAt(1).GetNamedString(L"name")));
            const auto savedApp = saved.GetObjectAt(0).GetNamedArray(L"applications").GetObjectAt(0);
            Assert::AreEqual(std::wstring(L"new.exe"), std::wstring(savedApp.GetNamedString(L"path")));
            Assert::AreEqual(std::wstring(L"Edited name"), std::wstring(saved.GetObjectAt(0).GetNamedString(L"name")));
        }

        TEST_METHOD (ApplicationMetadataUpdateRejectsConcurrentEditToSelectedWorkspace)
        {
            StoreFixture fixture;
            WorkspacesData::WorkspacesProject project;
            project.id = Id;
            project.name = L"Edited name";
            project.creationTime = 10;
            const auto originalWorkspace = WorkspacesData::WorkspacesProjectJSON::ToJson(project);
            json::JsonArray workspaces;
            workspaces.Append(originalWorkspace);
            json::JsonObject originalList;
            originalList.SetNamedValue(L"workspaces", workspaces);
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, originalList));

            auto updated = json::JsonObject::Parse(originalWorkspace.Stringify());
            updated.SetNamedValue(L"name", json::value(L"Stale launch snapshot"));
            auto concurrent = WorkspacesCli::ReadJson(fixture.file);
            concurrent->GetNamedArray(L"workspaces").GetObjectAt(0).SetNamedValue(L"name", json::value(L"New editor name"));
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, *concurrent));

            Assert::IsTrue(WorkspaceStore::UpdateApplicationMetadata(fixture.file, originalWorkspace, updated) == WorkspaceStore::UpdateResult::Conflict);
            const auto saved = WorkspacesCli::ReadJson(fixture.file)->GetNamedArray(L"workspaces").GetObjectAt(0);
            Assert::AreEqual(std::wstring(L"New editor name"), std::wstring(saved.GetNamedString(L"name")));
        }

        TEST_METHOD (DeletedWorkspaceIsNotRecreated)
        {
            StoreFixture fixture;
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, json::JsonObject::Parse(LR"({"workspaces":[]})")));
            Assert::IsTrue(WorkspaceStore::UpdateLastLaunched(fixture.file, Id, 100) == WorkspaceStore::UpdateResult::Conflict);
            Assert::AreEqual(0u, WorkspacesCli::ReadJson(fixture.file)->GetNamedArray(L"workspaces").Size());
        }

        TEST_METHOD (ReadOnlyDestinationReportsFailureAndKeepsOldData)
        {
            StoreFixture fixture;
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, Data()));
            Assert::IsTrue(SetFileAttributesW(fixture.file.c_str(), FILE_ATTRIBUTE_READONLY) != FALSE);
            Assert::IsTrue(WorkspaceStore::UpdateLastLaunched(fixture.file, Id, 100) == WorkspaceStore::UpdateResult::Failed);
            Assert::AreEqual(10.0, WorkspacesCli::ReadJson(fixture.file)->GetNamedArray(L"workspaces").GetObjectAt(0).GetNamedNumber(L"last-launched-time"));
        }

        TEST_METHOD (CooperatingWriterLockIsBounded)
        {
            StoreFixture fixture;
            Assert::IsTrue(WorkspaceStore::Write(fixture.file, Data()));
            wil::unique_handle lock(CreateFileW((fixture.file.wstring() + L".lock").c_str(),
                                                GENERIC_READ | GENERIC_WRITE,
                                                0,
                                                nullptr,
                                                OPEN_EXISTING,
                                                0,
                                                nullptr));
            Assert::IsTrue(static_cast<bool>(lock));
            const auto start = GetTickCount64();
            Assert::IsTrue(WorkspaceStore::UpdateLastLaunched(fixture.file, Id, 100) == WorkspaceStore::UpdateResult::Failed);
            Assert::IsTrue(GetTickCount64() - start < 5000);
        }
    };
}
