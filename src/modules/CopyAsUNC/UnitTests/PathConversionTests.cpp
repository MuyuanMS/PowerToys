#pragma warning(push)
#pragma warning(disable : 26466)
#include "CppUnitTest.h"
#pragma warning(pop)

#include "..\CopyAsUNCLib\PathConversion.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace CopyAsUNCUnitTests
{
    namespace
    {
        DWORD WriteUniversalName(std::wstring_view uncPath, LPVOID buffer, LPDWORD bufferSize)
        {
            const DWORD requiredSize = static_cast<DWORD>(sizeof(UNIVERSAL_NAME_INFOW) + ((uncPath.size() + 1) * sizeof(wchar_t)));
            if (*bufferSize < requiredSize)
            {
                *bufferSize = requiredSize;
                return ERROR_MORE_DATA;
            }

            auto info = static_cast<UNIVERSAL_NAME_INFOW*>(buffer);
            auto text = reinterpret_cast<wchar_t*>(info + 1);
            memcpy(text, uncPath.data(), uncPath.size() * sizeof(wchar_t));
            text[uncPath.size()] = L'\0';
            info->lpUniversalName = text;
            return NO_ERROR;
        }
    }

    TEST_CLASS(PathConversionTests)
    {
    public:
        TEST_METHOD(ResolvePathReturnsDirectUNCPathWithoutCallingResolver)
        {
            bool resolverCalled = false;
            const auto result = copy_as_unc::ResolvePath(L"\\\\server\\share\\file.txt", [&](PCWSTR, DWORD, LPVOID, LPDWORD) -> DWORD {
                resolverCalled = true;
                return ERROR_NOT_CONNECTED;
            });

            Assert::IsTrue(result.has_value());
            Assert::AreEqual(L"\\\\server\\share\\file.txt", result->c_str());
            Assert::IsFalse(resolverCalled);
        }

        TEST_METHOD(ResolvePathReturnsMappedPath)
        {
            const auto result = copy_as_unc::ResolvePath(L"Z:\\folder\\file.txt", [](PCWSTR, DWORD, LPVOID buffer, LPDWORD bufferSize) -> DWORD {
                return WriteUniversalName(L"\\\\server\\share\\folder\\file.txt", buffer, bufferSize);
            });

            Assert::IsTrue(result.has_value());
            Assert::AreEqual(L"\\\\server\\share\\folder\\file.txt", result->c_str());
        }

        TEST_METHOD(ResolvePathRetriesAfterErrorMoreData)
        {
            int calls = 0;
            const auto result = copy_as_unc::ResolvePath(L"Z:\\folder\\file.txt", [&](PCWSTR, DWORD, LPVOID buffer, LPDWORD bufferSize) -> DWORD {
                ++calls;
                if (calls == 1)
                {
                    *bufferSize = 4096;
                    return ERROR_MORE_DATA;
                }

                return WriteUniversalName(L"\\\\server\\share\\folder\\file.txt", buffer, bufferSize);
            });

            Assert::AreEqual(2, calls);
            Assert::IsTrue(result.has_value());
        }

        TEST_METHOD(ResolvePathReturnsEmptyForUnresolvedPath)
        {
            const auto result = copy_as_unc::ResolvePath(L"C:\\local.txt", [](PCWSTR, DWORD, LPVOID, LPDWORD) -> DWORD {
                return ERROR_NOT_CONNECTED;
            });

            Assert::IsFalse(result.has_value());
        }

        TEST_METHOD(BuildClipboardTextIncludesEveryResolvableSelection)
        {
            const std::vector<std::wstring> paths{
                L"\\\\server\\share\\one.txt",
                L"Z:\\two.txt",
                L"C:\\local.txt",
            };
            const auto result = copy_as_unc::BuildClipboardText(paths, [](PCWSTR path, DWORD, LPVOID buffer, LPDWORD bufferSize) -> DWORD {
                if (path[0] == L'Z')
                {
                    return WriteUniversalName(L"\\\\server\\share\\two.txt", buffer, bufferSize);
                }

                return ERROR_NOT_CONNECTED;
            });

            Assert::AreEqual(L"\\\\server\\share\\one.txt\r\n\\\\server\\share\\two.txt", result.c_str());
        }
    };
}
