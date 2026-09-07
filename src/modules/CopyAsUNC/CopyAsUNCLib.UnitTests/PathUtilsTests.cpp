#pragma warning(push)
#pragma warning(disable : 26466)
#include "CppUnitTest.h"
#pragma warning(pop)

#include "PathUtils.h"

#include <cstring>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace CopyAsUNCLibUnitTests
{
    namespace
    {
        int resolverCallCount = 0;
        bool requireResize = false;
        std::wstring universalName;
        std::wstring clipboardText;
        HWND clipboardOwner = nullptr;

        DWORD WriteUniversalName(LPVOID buffer, LPDWORD bufferSize)
        {
            const DWORD requiredSize = static_cast<DWORD>(
                sizeof(UNIVERSAL_NAME_INFOW) + ((universalName.size() + 1) * sizeof(wchar_t)));
            if (*bufferSize < requiredSize)
            {
                *bufferSize = requiredSize;
                return ERROR_MORE_DATA;
            }

            auto info = static_cast<UNIVERSAL_NAME_INFOW*>(buffer);
            auto text = reinterpret_cast<wchar_t*>(info + 1);
            std::memcpy(text, universalName.c_str(), (universalName.size() + 1) * sizeof(wchar_t));
            info->lpUniversalName = text;
            return NO_ERROR;
        }

        DWORD WINAPI FakeUniversalNameResolver(LPCWSTR, DWORD, LPVOID buffer, LPDWORD bufferSize)
        {
            ++resolverCallCount;
            if (requireResize && resolverCallCount == 1)
            {
                *bufferSize = static_cast<DWORD>(
                    sizeof(UNIVERSAL_NAME_INFOW) + ((MAX_PATH + 100) * sizeof(wchar_t)));
                return ERROR_MORE_DATA;
            }

            return WriteUniversalName(buffer, bufferSize);
        }

        DWORD WINAPI FailingUniversalNameResolver(LPCWSTR, DWORD, LPVOID, LPDWORD)
        {
            ++resolverCallCount;
            return ERROR_BAD_NET_NAME;
        }

        UINT WINAPI RemoteDriveType(LPCWSTR root)
        {
            return wcscmp(root, L"Z:\\") == 0 ? DRIVE_REMOTE : DRIVE_FIXED;
        }

        HRESULT CaptureClipboardText(HWND owner, std::wstring_view text)
        {
            clipboardOwner = owner;
            clipboardText.assign(text);
            return S_OK;
        }

        HRESULT FailClipboardWrite(HWND, std::wstring_view)
        {
            return E_ACCESSDENIED;
        }

        void ResetFakes()
        {
            resolverCallCount = 0;
            requireResize = false;
            universalName.clear();
            clipboardText.clear();
            clipboardOwner = nullptr;
        }
    }

    TEST_CLASS(PathUtilsTests)
    {
    public:
        TEST_METHOD_INITIALIZE(Initialize)
        {
            ResetFakes();
        }

        TEST_METHOD(IsCopyablePathAcceptsUNCAndMappedDrivePaths)
        {
            Assert::IsTrue(copy_as_unc::IsCopyablePath(L"\\\\server\\share\\file.txt", RemoteDriveType));
            Assert::IsTrue(copy_as_unc::IsCopyablePath(L"\\\\?\\UNC\\server\\share\\file.txt", RemoteDriveType));
            Assert::IsTrue(copy_as_unc::IsCopyablePath(L"Z:\\folder\\file.txt", RemoteDriveType));
        }

        TEST_METHOD(IsCopyablePathRejectsLocalAndMalformedPaths)
        {
            Assert::IsFalse(copy_as_unc::IsCopyablePath(L"C:\\folder\\file.txt", RemoteDriveType));
            Assert::IsFalse(copy_as_unc::IsCopyablePath(L"relative.txt", RemoteDriveType));
            Assert::IsFalse(copy_as_unc::IsCopyablePath(L"", RemoteDriveType));
            Assert::IsFalse(copy_as_unc::IsCopyablePath(L"\\\\?\\C:\\folder\\file.txt", RemoteDriveType));
            Assert::IsFalse(copy_as_unc::IsCopyablePath(L"\\\\.\\C:\\folder\\file.txt", RemoteDriveType));
        }

        TEST_METHOD(ResolveToUNCPathPreservesExistingUNCPath)
        {
            std::wstring result;
            Assert::AreEqual(
                S_OK,
                copy_as_unc::ResolveToUNCPath(
                    L"\\\\server\\share\\folder\\file.txt",
                    result,
                    FakeUniversalNameResolver));
            Assert::AreEqual(L"\\\\server\\share\\folder\\file.txt", result.c_str());
            Assert::AreEqual(0, resolverCallCount);
        }

        TEST_METHOD(ResolveToUNCPathPreservesExtendedUNCPath)
        {
            std::wstring result;
            Assert::AreEqual(
                S_OK,
                copy_as_unc::ResolveToUNCPath(
                    L"\\\\?\\UNC\\server\\share\\folder\\file.txt",
                    result,
                    FakeUniversalNameResolver));
            Assert::AreEqual(L"\\\\?\\UNC\\server\\share\\folder\\file.txt", result.c_str());
            Assert::AreEqual(0, resolverCallCount);
        }

        TEST_METHOD(ResolveToUNCPathRejectsLocalDevicePaths)
        {
            for (const std::wstring_view path : { L"\\\\?\\C:\\folder\\file.txt", L"\\\\.\\C:\\folder\\file.txt" })
            {
                std::wstring result;
                Assert::AreEqual(
                    HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED),
                    copy_as_unc::ResolveToUNCPath(path, result, FakeUniversalNameResolver));
                Assert::IsTrue(result.empty());
            }

            Assert::AreEqual(0, resolverCallCount);
        }

        TEST_METHOD(ResolveToUNCPathResolvesMappedDrive)
        {
            universalName = L"\\\\server\\share\\folder\\file.txt";
            std::wstring result;

            Assert::AreEqual(
                S_OK,
                copy_as_unc::ResolveToUNCPath(L"Z:\\folder\\file.txt", result, FakeUniversalNameResolver));
            Assert::AreEqual(universalName.c_str(), result.c_str());
            Assert::AreEqual(1, resolverCallCount);
        }

        TEST_METHOD(ResolveToUNCPathRetriesWithRequiredBuffer)
        {
            requireResize = true;
            universalName = L"\\\\server\\share\\long\\folder\\file.txt";
            std::wstring result;

            Assert::AreEqual(
                S_OK,
                copy_as_unc::ResolveToUNCPath(L"Z:\\long\\folder\\file.txt", result, FakeUniversalNameResolver));
            Assert::AreEqual(universalName.c_str(), result.c_str());
            Assert::AreEqual(2, resolverCallCount);
        }

        TEST_METHOD(ResolveToUNCPathPropagatesNetworkFailure)
        {
            std::wstring result;
            Assert::AreEqual(
                HRESULT_FROM_WIN32(ERROR_BAD_NET_NAME),
                copy_as_unc::ResolveToUNCPath(L"Z:\\missing.txt", result, FailingUniversalNameResolver));
            Assert::IsTrue(result.empty());
        }

        TEST_METHOD(ResolveAndCopyPathWritesResolvedPathAndOwner)
        {
            universalName = L"\\\\server\\share\\folder\\file.txt";
            const HWND expectedOwner = reinterpret_cast<HWND>(123);

            Assert::AreEqual(
                S_OK,
                copy_as_unc::ResolveAndCopyPath(
                    L"Z:\\folder\\file.txt",
                    expectedOwner,
                    FakeUniversalNameResolver,
                    CaptureClipboardText));
            Assert::AreEqual(universalName.c_str(), clipboardText.c_str());
            Assert::IsTrue(clipboardOwner == expectedOwner);
        }

        TEST_METHOD(ResolveAndCopyPathDoesNotWriteAfterResolutionFailure)
        {
            Assert::AreEqual(
                HRESULT_FROM_WIN32(ERROR_BAD_NET_NAME),
                copy_as_unc::ResolveAndCopyPath(
                    L"Z:\\missing.txt",
                    nullptr,
                    FailingUniversalNameResolver,
                    CaptureClipboardText));
            Assert::IsTrue(clipboardText.empty());
        }

        TEST_METHOD(ResolveAndCopyPathPropagatesClipboardFailure)
        {
            Assert::AreEqual(
                E_ACCESSDENIED,
                copy_as_unc::ResolveAndCopyPath(
                    L"\\\\server\\share\\file.txt",
                    nullptr,
                    FakeUniversalNameResolver,
                    FailClipboardWrite));
            Assert::AreEqual(0, resolverCallCount);
        }
    };
}
