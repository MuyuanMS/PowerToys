#include "pch.h"

#include "../WorkspacesSettingsClient/PTSettingsClient.h"
#include "../WorkspacesSettingsService/Bindings.h"
#include "../WorkspacesSettingsService/protocol/PipeName.h"

#include <thread>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace WorkspacesLibUnitTests
{
    TEST_CLASS(PTSettingsClientTests)
    {
    public:
        TEST_METHOD(RejectsPredictablePipeOwnedByUntrustedProcess)
        {
            const std::wstring pipeName = PTSettingsSvc::BuildPipeName(
                PTSettingsSvc::CurrentProcessUserSidString());
            HANDLE server = CreateNamedPipeW(
                pipeName.c_str(),
                PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                1,
                64,
                64,
                0,
                nullptr);
            if (server == INVALID_HANDLE_VALUE)
            {
                // A developer may run the suite while the real service owns this
                // per-user pipe. CI has no installed service and exercises the
                // spoof path below.
                return;
            }

            std::thread serverThread([server]() {
                ConnectNamedPipe(server, nullptr);
                DisconnectNamedPipe(server);
                CloseHandle(server);
            });

            const auto result = PTSettingsClient::Ping();
            serverThread.join();

            Assert::IsTrue(
                result == PTSettingsClient::Result::ServiceUnavailable,
                L"The client trusted a same-user process squatting on the predictable pipe name.");
        }

        TEST_METHOD(ManagementSidValidationRejectsPathLikeInput)
        {
            Assert::IsTrue(PTSettingsSvc::IsValidSidString(
                PTSettingsSvc::CurrentProcessUserSidString()));
            Assert::IsFalse(PTSettingsSvc::IsValidSidString(
                L"..\\attacker-controlled"));
        }

        TEST_METHOD(NamespaceValidationRejectsPathComponents)
        {
            Assert::IsTrue(PTSettingsSvc::IsValidNamespaceId(L"Workspaces"));
            Assert::IsTrue(PTSettingsSvc::IsValidNamespaceId(L"Workspaces.v2"));
            Assert::IsFalse(PTSettingsSvc::IsValidNamespaceId(L"."));
            Assert::IsFalse(PTSettingsSvc::IsValidNamespaceId(L".."));
        }
    };
}
