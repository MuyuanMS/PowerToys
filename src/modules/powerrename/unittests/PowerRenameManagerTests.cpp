#include "pch.h"
#include <PowerRenameInterfaces.h>
#include <PowerRenameManager.h>
#include <PowerRenameItem.h>
#include "MockPowerRenameItem.h"
#include "MockPowerRenameManagerEvents.h"
#include "TestFileHelper.h"
#include "Helpers.h"
#include <atomic>
#include <thread>
#include <vector>

#define DEFAULT_FLAGS 0

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

EXTERN_C IMAGE_DOS_HEADER __ImageBase;

#define HINST_THISCOMPONENT ((HINSTANCE)&__ImageBase)

HINSTANCE g_hostHInst = HINST_THISCOMPONENT;

namespace PowerRenameManagerTests
{
    namespace
    {
        class CConcurrentReadPowerRenameManagerEvents : public CMockPowerRenameManagerEvents
        {
        public:
            IFACEMETHODIMP OnRegExStarted(_In_ DWORD threadId) override
            {
                m_regExStartedSignal = true;
                return CMockPowerRenameManagerEvents::OnRegExStarted(threadId);
            }

            IFACEMETHODIMP OnRegExCompleted(_In_ DWORD threadId) override
            {
                m_regExCompletedSignal = true;
                return CMockPowerRenameManagerEvents::OnRegExCompleted(threadId);
            }

            std::atomic_bool m_regExStartedSignal{ false };
            std::atomic_bool m_regExCompletedSignal{ false };
        };
    }

    TEST_CLASS (SimpleTests)
    {
    public:
        struct rename_pairs
        {
            std::wstring originalName;
            std::wstring newName;
            bool isFile;
            bool shouldRename;
            int depth;
        };

        void RenameHelper(_In_ rename_pairs * renamePairs, _In_ int numPairs, _In_ std::wstring searchTerm, _In_ std::wstring replaceTerm, SYSTEMTIME fileTime, _In_ DWORD flags)
        {
            // Create a single item (in a temp directory) and verify rename works as expected
            CTestFileHelper testFileHelper;
            for (int i = 0; i < numPairs; i++)
            {
                if (renamePairs[i].isFile)
                {
                    Assert::IsTrue(testFileHelper.AddFile(renamePairs[i].originalName));
                }
                else
                {
                    Assert::IsTrue(testFileHelper.AddFolder(renamePairs[i].originalName));
                }
            }

            CComPtr<IPowerRenameManager> mgr;
            Assert::IsTrue(CPowerRenameManager::s_CreateInstance(&mgr) == S_OK);
            CMockPowerRenameManagerEvents* mockMgrEvents = new CMockPowerRenameManagerEvents();
            CComPtr<IPowerRenameManagerEvents> mgrEvents;
            Assert::IsTrue(mockMgrEvents->QueryInterface(IID_PPV_ARGS(&mgrEvents)) == S_OK);
            DWORD cookie = 0;
            Assert::IsTrue(mgr->Advise(mgrEvents, &cookie) == S_OK);

            for (int i = 0; i < numPairs; i++)
            {
                CComPtr<IPowerRenameItem> item;
                CMockPowerRenameItem::CreateInstance(testFileHelper.GetFullPath(renamePairs[i].originalName).c_str(),
                                                     renamePairs[i].originalName.c_str(),
                                                     renamePairs[i].depth,
                                                     !renamePairs[i].isFile,
                                                     fileTime,
                                                     &item);

                int itemId = 0;
                Assert::IsTrue(item->GetId(&itemId) == S_OK);
                mgr->AddItem(item);
            }

            // TODO: Setup match and replace parameters
            CComPtr<IPowerRenameRegEx> renRegEx;
            Assert::IsTrue(mgr->GetRenameRegEx(&renRegEx) == S_OK);
            renRegEx->PutFlags(flags);
            renRegEx->PutSearchTerm(searchTerm.c_str());
            renRegEx->PutReplaceTerm(replaceTerm.c_str());

            // Perform the rename
            bool replaceSuccess = false;
            for (int step = 0; step < 20; step++)
            {
                replaceSuccess = mgr->Rename(0, true) == S_OK;
                if (replaceSuccess)
                {
                    break;
                }
                Sleep(10);
            }

            Assert::IsTrue(replaceSuccess);

            std::vector<std::wstring> shouldRename = { L"not ", L"" };

            // Verify the rename occurred
            for (int i = 0; i < numPairs; i++)
            {
                Assert::IsTrue(testFileHelper.PathExistsCaseSensitive(renamePairs[i].originalName) == !renamePairs[i].shouldRename, 
                               (std::wstring(L"The path: [" +  renamePairs[i].originalName + L"] should ") + shouldRename[!renamePairs[i].shouldRename] + L"exist.").c_str());
                Assert::IsTrue(testFileHelper.PathExistsCaseSensitive(renamePairs[i].newName) == renamePairs[i].shouldRename,
                               (std::wstring(L"The path: [" + renamePairs[i].newName + L"] should ") + shouldRename[renamePairs[i].shouldRename] + L"exist.").c_str());
            }

            Assert::IsTrue(mgr->Shutdown() == S_OK);

            mockMgrEvents->Release();
        }
        TEST_METHOD (CreateTest)
        {
            CComPtr<IPowerRenameManager> mgr;
            Assert::IsTrue(CPowerRenameManager::s_CreateInstance(&mgr) == S_OK);
        }

        TEST_METHOD (CreateAndShutdownTest)
        {
            CComPtr<IPowerRenameManager> mgr;
            Assert::IsTrue(CPowerRenameManager::s_CreateInstance(&mgr) == S_OK);
            Assert::IsTrue(mgr->Shutdown() == S_OK);
        }

        TEST_METHOD (AddItemTest)
        {
            CComPtr<IPowerRenameManager> mgr;
            Assert::IsTrue(CPowerRenameManager::s_CreateInstance(&mgr) == S_OK);
            CComPtr<IPowerRenameItem> item;
            CMockPowerRenameItem::CreateInstance(L"foo", L"foo", 0, false, SYSTEMTIME{ 0 }, &item);
            mgr->AddItem(item);
            Assert::IsTrue(mgr->Shutdown() == S_OK);
        }

        TEST_METHOD (VerifyRenameManagerEvents)
        {
            CComPtr<IPowerRenameManager> mgr;
            Assert::IsTrue(CPowerRenameManager::s_CreateInstance(&mgr) == S_OK);
            CMockPowerRenameManagerEvents* mockMgrEvents = new CMockPowerRenameManagerEvents();
            CComPtr<IPowerRenameManagerEvents> mgrEvents;
            Assert::IsTrue(mockMgrEvents->QueryInterface(IID_PPV_ARGS(&mgrEvents)) == S_OK);
            DWORD cookie = 0;
            Assert::IsTrue(mgr->Advise(mgrEvents, &cookie) == S_OK);
            CComPtr<IPowerRenameItem> item;
            CMockPowerRenameItem::CreateInstance(L"foo", L"foo", 0, false, SYSTEMTIME{ 0 }, &item);
            int itemId = 0;
            Assert::IsTrue(item->GetId(&itemId) == S_OK);
            mgr->AddItem(item);

            Assert::IsTrue(mgr->Shutdown() == S_OK);

            mockMgrEvents->Release();
        }

        TEST_METHOD (VerifySingleRename)
        {
            // Create a single item and verify rename works as expected
            rename_pairs renamePairs[] = {
                { L"foo.txt", L"bar.txt", true, true }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS);
        }

        TEST_METHOD (VerifyMultiRename)
        {
            // Create a single item and verify rename works as expected
            rename_pairs renamePairs[] = {
                { L"foo1.txt", L"bar1.txt", true, true, 0 },
                { L"foo2.txt", L"bar2.txt", true, true, 0 },
                { L"foo3.txt", L"bar3.txt", true, true, 0 },
                { L"foo4.txt", L"bar4.txt", true, true, 0 },
                { L"foo5.txt", L"bar5.txt", true, true, 0 },
                { L"baa.txt", L"baa_norename.txt", true, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS);
        }

        TEST_METHOD (VerifyFilesOnlyRename)
        {
            // Verify only files are renamed when folders match too
            rename_pairs renamePairs[] = {
                { L"foo.txt", L"bar.txt", true, true, 0 },
                { L"foo", L"foo_norename", false, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | ExcludeFolders);
        }

        TEST_METHOD (VerifyFoldersOnlyRename)
        {
            // Verify only folders are renamed when files match too
            rename_pairs renamePairs[] = {
                { L"foo.txt", L"foo_norename.txt", true, false, 0 },
                { L"foo", L"bar", false, true, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | ExcludeFiles);
        }

        TEST_METHOD (VerifyFileNameOnlyRename)
        {
            // Verify only file name is renamed, not extension
            rename_pairs renamePairs[] = {
                { L"foo.foo", L"bar.foo", true, true, 0 },
                { L"test.foo", L"test.foo_norename", true, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | NameOnly);
        }

        TEST_METHOD (VerifyFileExtensionOnlyRename)
        {
            // Verify only file extension is renamed, not name
            rename_pairs renamePairs[] = {
                { L"foo.foo", L"foo.bar", true, true, 0 },
                { L"test.foo", L"test.bar", true, true, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | ExtensionOnly);
        }

        TEST_METHOD (VerifySubFoldersRename)
        {
            // Verify subfolders do not get renamed
            rename_pairs renamePairs[] = {
                { L"foo1", L"bar1", false, true, 0 },
                { L"foo2", L"foo2_norename", false, false, 1 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | ExcludeSubfolders);
        }

        TEST_METHOD (VerifyUppercaseTransform)
        {
            rename_pairs renamePairs[] = {
                { L"foo", L"BAR", true, true, 0 },
                { L"foo.test", L"BAR.TEST", true, true, 0 },
                { L"TEST", L"TEST_norename", true, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | Uppercase);
        }

        TEST_METHOD (VerifyLowercaseTransform)
        {
            rename_pairs renamePairs[] = {
                { L"Foo", L"bar", false, true, 0 },
                { L"Foo.teST", L"bar.test", false, true, 0 },
                { L"test", L"test_norename", false, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | Lowercase);
        }

        TEST_METHOD (VerifyTitlecaseTransform)
        {
            rename_pairs renamePairs[] = {
                { L"foo And The To", L"Bar and the To", false, true, 0 },
                { L"foo And The To.txt", L"Bar and the To.txt", true, true, 0 },
                { L"Test", L"Test_norename", false, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | Titlecase);
        }      

        TEST_METHOD (VerifyTitlecaseWithApostropheTransform)
        {
            rename_pairs renamePairs[] = {
                { L"the foo i'll and i've you're dogs' the i'd it's i'm don't to y'all", L"The Bar I'll and I've You're Dogs' the I'd It's I'm Don't to Y'all", false, true, 0 },
                { L"'the 'foo' 'i'll' and i've you're dogs' the 'i'd' it's i'm don't to y'all.txt", L"'The 'Bar' 'I'll' and I've You're Dogs' the 'I'd' It's I'm Don't to Y'all.txt", true, true, 0 },
                { L"Test", L"Test_norename", false, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | Titlecase);
        }

        TEST_METHOD (VerifyCapitalizedTransform)
        {
            rename_pairs renamePairs[] = {
                { L"foo and the to", L"Bar And The To", false, true, 0 },
                { L"Test", L"Test_norename", false, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | Capitalized);
        }

        TEST_METHOD (VerifyCapitalizedWithApostropheTransform)
        {
            rename_pairs renamePairs[] = {
                { L"foo i'll and i've you're dogs' the i'd it's i'm don't to y'all", L"Bar I'll And I've You're Dogs' The I'd It's I'm Don't To Y'all", false, true, 0 },
                { L"'foo i'll 'and' i've you're dogs' the i'd it's i'm don't to y'all.txt", L"'Bar I'll 'And' I've You're Dogs' The I'd It's I'm Don't To Y'all.txt", true, true, 0 },
                { L"Test", L"Test_norename", false, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | Capitalized);
        }

        TEST_METHOD (VerifyNameOnlyTransform)
        {
            rename_pairs renamePairs[] = {
                { L"foo.foo", L"BAR.foo", true, true, 0 },
                { L"foo.txt", L"BAR.TXT", false, true, 0 },
                { L"TEST", L"TEST_norename", false, false, 1 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | Uppercase | NameOnly);
        }

        TEST_METHOD (VerifyExtensionOnlyTransform)
        {
            rename_pairs renamePairs[] = {
                { L"foo.FOO", L"foo.bar", true, true, 0 },
                { L"bar.FOO", L"bar.FOO_norename", false, false, 0 },
                { L"foo.bar", L"foo.bar_norename", true, false, 0 }
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS | Lowercase | ExtensionOnly);
        }

        TEST_METHOD (VerifyFileAttributesNoPadding)
        {
            rename_pairs renamePairs[] = {
                { L"foo", L"bar20-7-22-15-6-42-4", true, true, 0 },
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar$YY-$M-$D-$h-$m-$s-$f", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS);
        }

        TEST_METHOD (VerifyFileAttributesPadding)
        {
            rename_pairs renamePairs[] = {
                { L"foo", L"bar2020-07-22-15-06-42-453", true, true, 0 },
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar$YYYY-$MM-$DD-$hh-$mm-$ss-$fff", SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS);
        }

        TEST_METHOD (VerifyFileAttributesMonthAndDayNames)
        {
            SYSTEMTIME fileTime = { 2020, 1, 3, 1, 15, 6, 42, 453 };
            wchar_t localeName[LOCALE_NAME_MAX_LENGTH];
            wchar_t result[MAX_PATH] = L"bar";
            wchar_t formattedDate[MAX_PATH];
            wchar_t upper;
            if (GetUserDefaultLocaleName(localeName, LOCALE_NAME_MAX_LENGTH) == 0)
                StringCchCopy(localeName, LOCALE_NAME_MAX_LENGTH, L"en_US");

            GetDateFormatEx(localeName, NULL, &fileTime, L"MMM", formattedDate, MAX_PATH, NULL);
            LCMapStringEx(localeName, LCMAP_UPPERCASE | LCMAP_LINGUISTIC_CASING, &formattedDate[0], 1, &upper, 1, NULL, NULL, 0);
            formattedDate[0] = upper;
            StringCchPrintf(result, MAX_PATH, TEXT("%s%s"), result, formattedDate);

            GetDateFormatEx(localeName, NULL, &fileTime, L"MMMM", formattedDate, MAX_PATH, NULL);
            LCMapStringEx(localeName, LCMAP_UPPERCASE | LCMAP_LINGUISTIC_CASING, &formattedDate[0], 1, &upper, 1, NULL, NULL, 0);
            formattedDate[0] = upper;
            StringCchPrintf(result, MAX_PATH, TEXT("%s-%s"), result, formattedDate);

            GetDateFormatEx(localeName, NULL, &fileTime, L"ddd", formattedDate, MAX_PATH, NULL);
            LCMapStringEx(localeName, LCMAP_UPPERCASE | LCMAP_LINGUISTIC_CASING, &formattedDate[0], 1, &upper, 1, NULL, NULL, 0);
            formattedDate[0] = upper;
            StringCchPrintf(result, MAX_PATH, TEXT("%s-%s"), result, formattedDate);

            GetDateFormatEx(localeName, NULL, &fileTime, L"dddd", formattedDate, MAX_PATH, NULL);
            LCMapStringEx(localeName, LCMAP_UPPERCASE | LCMAP_LINGUISTIC_CASING, &formattedDate[0], 1, &upper, 1, NULL, NULL, 0);
            formattedDate[0] = upper;
            StringCchPrintf(result, MAX_PATH, TEXT("%s-%s"), result, formattedDate);

            rename_pairs renamePairs[] = {
                { L"foo", result, true, true, 0 },
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar$MMM-$MMMM-$DDD-$DDDD", SYSTEMTIME{ 2020, 1, 3, 1, 15, 6, 42, 453 }, DEFAULT_FLAGS);
        }

        // Regression test: rename with non-ASCII (Simplified Chinese) filenames should not hang.
        // This exercises the regex worker event sequencing and exclusive-lock paths added to fix
        // the AppHangTransient reported when processing Chinese filenames.
        TEST_METHOD (VerifyNonAsciiRename)
        {
            rename_pairs renamePairs[] = {
                { L"\u6d4b\u8bd5\u6587\u4ef6.txt", L"\u65b0\u6587\u4ef6.txt", true, true, 0 },
                { L"\u6d4b\u8bd5\u6587\u4ef6\u4e8c.txt", L"\u65b0\u6587\u4ef6\u4e8c.txt", true, true, 0 },
            };
            // Search for Chinese "test file" prefix and replace with Chinese "new file"
            RenameHelper(renamePairs, ARRAYSIZE(renamePairs),
                         L"\u6d4b\u8bd5", L"\u65b0",
                         SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, DEFAULT_FLAGS);
        }

        // Regression test: changing the search term multiple times before Apply completes must not
        // leave the reentrancy flag permanently set or corrupt the shared regex events.
        TEST_METHOD (VerifyRepeatedSearchTermChange)
        {
            CTestFileHelper testFileHelper;
            Assert::IsTrue(testFileHelper.AddFile(L"foo.txt"));

            CComPtr<IPowerRenameManager> mgr;
            Assert::IsTrue(CPowerRenameManager::s_CreateInstance(&mgr) == S_OK);
            CMockPowerRenameManagerEvents* mockMgrEvents = new CMockPowerRenameManagerEvents();
            CComPtr<IPowerRenameManagerEvents> mgrEvents;
            Assert::IsTrue(mockMgrEvents->QueryInterface(IID_PPV_ARGS(&mgrEvents)) == S_OK);
            DWORD cookie = 0;
            Assert::IsTrue(mgr->Advise(mgrEvents, &cookie) == S_OK);

            CComPtr<IPowerRenameItem> item;
            CMockPowerRenameItem::CreateInstance(
                testFileHelper.GetFullPath(L"foo.txt").c_str(), L"foo.txt", 0, false,
                SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, &item);
            mgr->AddItem(item);

            CComPtr<IPowerRenameRegEx> renRegEx;
            Assert::IsTrue(mgr->GetRenameRegEx(&renRegEx) == S_OK);
            renRegEx->PutFlags(DEFAULT_FLAGS);

            // Simulate rapid search term changes (cancels previous worker each time).
            renRegEx->PutSearchTerm(L"foo");
            renRegEx->PutSearchTerm(L"f");
            renRegEx->PutSearchTerm(L"foo");
            renRegEx->PutReplaceTerm(L"bar");

            // Rename should succeed; verifies no event-sequencing deadlock after repeated cancel/restart.
            bool replaceSuccess = false;
            for (int step = 0; step < 20; step++)
            {
                replaceSuccess = mgr->Rename(0, true) == S_OK;
                if (replaceSuccess)
                {
                    break;
                }
                Sleep(10);
            }
            Assert::IsTrue(replaceSuccess);
            Assert::IsTrue(testFileHelper.PathExistsCaseSensitive(L"bar.txt"));

            Assert::IsTrue(mgr->Shutdown() == S_OK);
            mockMgrEvents->Release();
        }

        TEST_METHOD(VerifyConcurrentPreviewReadsDuringRegEx)
        {
            CComPtr<IPowerRenameManager> mgr;
            Assert::IsTrue(CPowerRenameManager::s_CreateInstance(&mgr) == S_OK);

            auto* mockMgrEvents = new CConcurrentReadPowerRenameManagerEvents();
            CComPtr<IPowerRenameManagerEvents> mgrEvents;
            Assert::IsTrue(mockMgrEvents->QueryInterface(IID_PPV_ARGS(&mgrEvents)) == S_OK);
            DWORD cookie = 0;
            Assert::IsTrue(mgr->Advise(mgrEvents, &cookie) == S_OK);

            std::vector<CComPtr<IPowerRenameItem>> items;
            constexpr int itemCount = 128;
            for (int i = 0; i < itemCount; i++)
            {
                CComPtr<IPowerRenameItem> item;
                wchar_t fileName[MAX_PATH] = {};
                StringCchPrintf(fileName, ARRAYSIZE(fileName), L"\u6d4b\u8bd5\u6587\u4ef6_%03d.txt", i);
                CMockPowerRenameItem::CreateInstance(fileName, fileName, 0, false, SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, &item);
                items.push_back(item);
                mgr->AddItem(item);
            }

            CComPtr<IPowerRenameRegEx> renRegEx;
            Assert::IsTrue(mgr->GetRenameRegEx(&renRegEx) == S_OK);
            renRegEx->PutFlags(DEFAULT_FLAGS);

            std::atomic_bool keepReading{ true };
            std::atomic_bool readSucceeded{ true };
            std::thread reader([&]() {
                const HRESULT coInit = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                while (!mockMgrEvents->m_regExStartedSignal.load() && !mockMgrEvents->m_regExCompletedSignal.load())
                {
                    Sleep(1);
                }

                while (keepReading.load())
                {
                    for (const auto& item : items)
                    {
                        PWSTR newName = nullptr;
                        readSucceeded = readSucceeded.load() && SUCCEEDED(item->GetNewName(&newName));

                        bool shouldRename = false;
                        readSucceeded = readSucceeded.load() && SUCCEEDED(item->ShouldRenameItem(DEFAULT_FLAGS, &shouldRename));

                        PowerRenameItemRenameStatus status{};
                        readSucceeded = readSucceeded.load() && SUCCEEDED(item->GetStatus(&status));
                        CoTaskMemFree(newName);
                    }
                }

                if (SUCCEEDED(coInit))
                {
                    CoUninitialize();
                }
            });

            renRegEx->PutSearchTerm(L"\u6d4b\u8bd5");
            renRegEx->PutReplaceTerm(L"\u65b0");

            for (int attempt = 0; attempt < 200 && !mockMgrEvents->m_regExCompletedSignal.load(); attempt++)
            {
                Sleep(5);
            }

            keepReading = false;
            reader.join();

            Assert::IsTrue(readSucceeded.load());
            Assert::IsTrue(mockMgrEvents->m_regExCompletedSignal.load());
            Assert::IsTrue(mgr->Shutdown() == S_OK);
            mockMgrEvents->Release();
        }

        TEST_METHOD(VerifyKeepOpenRenameCanRepreviewNonAsciiItem)
        {
            CTestFileHelper testFileHelper;
            Assert::IsTrue(testFileHelper.AddFile(L"\u6d4b\u8bd5\u6587\u4ef6.txt"));

            CComPtr<IPowerRenameManager> mgr;
            Assert::IsTrue(CPowerRenameManager::s_CreateInstance(&mgr) == S_OK);
            CMockPowerRenameManagerEvents* mockMgrEvents = new CMockPowerRenameManagerEvents();
            CComPtr<IPowerRenameManagerEvents> mgrEvents;
            Assert::IsTrue(mockMgrEvents->QueryInterface(IID_PPV_ARGS(&mgrEvents)) == S_OK);
            DWORD cookie = 0;
            Assert::IsTrue(mgr->Advise(mgrEvents, &cookie) == S_OK);

            CComPtr<IPowerRenameItem> item;
            CMockPowerRenameItem::CreateInstance(
                testFileHelper.GetFullPath(L"\u6d4b\u8bd5\u6587\u4ef6.txt").c_str(),
                L"\u6d4b\u8bd5\u6587\u4ef6.txt",
                0,
                false,
                SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 },
                &item);
            mgr->AddItem(item);

            CComPtr<IPowerRenameRegEx> renRegEx;
            Assert::IsTrue(mgr->GetRenameRegEx(&renRegEx) == S_OK);
            renRegEx->PutFlags(DEFAULT_FLAGS);
            renRegEx->PutSearchTerm(L"\u6d4b\u8bd5");
            renRegEx->PutReplaceTerm(L"\u65b0");

            Assert::IsTrue(mgr->Rename(0, false) == S_OK);
            Assert::IsTrue(testFileHelper.PathExistsCaseSensitive(L"\u65b0\u6587\u4ef6.txt"));

            PWSTR originalName = nullptr;
            Assert::IsTrue(SUCCEEDED(item->GetOriginalName(&originalName)));
            Assert::AreEqual(L"\u65b0\u6587\u4ef6.txt", originalName);
            CoTaskMemFree(originalName);

            mockMgrEvents->m_regExCompleted = false;
            renRegEx->PutSearchTerm(L"\u65b0");
            renRegEx->PutReplaceTerm(L"\u6700\u7ec8", true);

            for (int attempt = 0; attempt < 200 && !mockMgrEvents->m_regExCompleted; attempt++)
            {
                Sleep(5);
            }

            PWSTR newName = nullptr;
            Assert::IsTrue(SUCCEEDED(item->GetNewName(&newName)));
            Assert::AreEqual(L"\u6700\u7ec8\u6587\u4ef6.txt", newName);
            CoTaskMemFree(newName);

            Assert::IsTrue(mgr->Shutdown() == S_OK);
            mockMgrEvents->Release();
        }
    };
}
