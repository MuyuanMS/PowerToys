#include "pch.h"
#include <PowerRenameInterfaces.h>
#include <PowerRenameManager.h>
#include <PowerRenameItem.h>
#include "MockPowerRenameItem.h"
#include "MockPowerRenameManagerEvents.h"
#include "TestFileHelper.h"
#include "Helpers.h"

#define DEFAULT_FLAGS 0

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

EXTERN_C IMAGE_DOS_HEADER __ImageBase;

#define HINST_THISCOMPONENT ((HINSTANCE)&__ImageBase)

HINSTANCE g_hostHInst = HINST_THISCOMPONENT;

namespace PowerRenameManagerTests
{
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

        static std::wstring GetItemStringValue(_In_ IPowerRenameItem* item, _In_ HRESULT(__stdcall IPowerRenameItem::* getter)(_Outptr_ PWSTR*))
        {
            PWSTR value = nullptr;
            std::wstring result;
            if (SUCCEEDED((item->*getter)(&value)) && value != nullptr)
            {
                result = value;
            }
            CoTaskMemFree(value);
            return result;
        }

        static CComPtr<IPowerRenameManager> CreateManagerWithEvents(_Out_ CMockPowerRenameManagerEvents** mockMgrEvents)
        {
            CComPtr<IPowerRenameManager> mgr;
            Assert::AreEqual(S_OK, CPowerRenameManager::s_CreateInstance(&mgr));

            *mockMgrEvents = new CMockPowerRenameManagerEvents();
            CComPtr<IPowerRenameManagerEvents> mgrEvents;
            Assert::AreEqual(S_OK, (*mockMgrEvents)->QueryInterface(IID_PPV_ARGS(&mgrEvents)));

            DWORD cookie = 0;
            Assert::AreEqual(S_OK, mgr->Advise(mgrEvents, &cookie));

            return mgr;
        }

        static bool RenameWithRetries(_In_ IPowerRenameManager* mgr, bool closeWindow)
        {
            for (int step = 0; step < 20; step++)
            {
                if (mgr->Rename(0, closeWindow) == S_OK)
                {
                    return true;
                }
                Sleep(10);
            }
            return false;
        }

        static void ConfigureRenameRegex(_In_ IPowerRenameManager* mgr, _In_ std::wstring searchTerm, _In_ std::wstring replaceTerm, _In_ DWORD flags)
        {
            CComPtr<IPowerRenameRegEx> renRegEx;
            Assert::AreEqual(S_OK, mgr->GetRenameRegEx(&renRegEx));
            renRegEx->PutFlags(flags);
            renRegEx->PutSearchTerm(searchTerm.c_str());
            renRegEx->PutReplaceTerm(replaceTerm.c_str());
        }

        void RenameHelper(_In_ rename_pairs * renamePairs, _In_ int numPairs, _In_ std::wstring searchTerm, _In_ std::wstring replaceTerm, SYSTEMTIME fileTime, _In_ DWORD flags, bool closeWindow = true)
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

            CMockPowerRenameManagerEvents* mockMgrEvents = nullptr;
            CComPtr<IPowerRenameManager> mgr = CreateManagerWithEvents(&mockMgrEvents);

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
                Assert::AreEqual(S_OK, item->GetId(&itemId));
                mgr->AddItem(item);
            }

            ConfigureRenameRegex(mgr, searchTerm, replaceTerm, flags);

            // Perform the rename
            Assert::IsTrue(RenameWithRetries(mgr, closeWindow));

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

        TEST_METHOD (VerifyKeepUiOpenRenameUpdatesItemStateAndSecondApplyIsStable)
        {
            CTestFileHelper testFileHelper;
            Assert::IsTrue(testFileHelper.AddFile(L"foo.txt"));

            CMockPowerRenameManagerEvents* mockMgrEvents = nullptr;
            CComPtr<IPowerRenameManager> mgr = CreateManagerWithEvents(&mockMgrEvents);
            CComPtr<IPowerRenameItem> item;
            CMockPowerRenameItem::CreateInstance(testFileHelper.GetFullPath(L"foo.txt").c_str(),
                                                 L"foo.txt",
                                                 0,
                                                 false,
                                                 SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 },
                                                 &item);
            mgr->AddItem(item);

            ConfigureRenameRegex(mgr, L"foo", L"bar", DEFAULT_FLAGS);

            Assert::IsTrue(RenameWithRetries(mgr, false));

            CComPtr<IPowerRenameItem> updatedItem;
            Assert::AreEqual(S_OK, mgr->GetItemByIndex(0, &updatedItem));

            auto pathValue = GetItemStringValue(updatedItem, &IPowerRenameItem::GetPath);
            auto originalNameValue = GetItemStringValue(updatedItem, &IPowerRenameItem::GetOriginalName);
            auto newNameValue = GetItemStringValue(updatedItem, &IPowerRenameItem::GetNewName);
            Assert::AreEqual(testFileHelper.GetFullPath(L"bar.txt").c_str(), pathValue.c_str());
            Assert::AreEqual(L"bar.txt", originalNameValue.c_str());
            Assert::AreEqual(L"", newNameValue.c_str());
            Assert::IsTrue(testFileHelper.PathExistsCaseSensitive(L"bar.txt"));
            Assert::IsFalse(testFileHelper.PathExistsCaseSensitive(L"foo.txt"));

            Assert::IsTrue(RenameWithRetries(mgr, false));

            pathValue = GetItemStringValue(updatedItem, &IPowerRenameItem::GetPath);
            originalNameValue = GetItemStringValue(updatedItem, &IPowerRenameItem::GetOriginalName);
            newNameValue = GetItemStringValue(updatedItem, &IPowerRenameItem::GetNewName);
            Assert::AreEqual(testFileHelper.GetFullPath(L"bar.txt").c_str(), pathValue.c_str());
            Assert::AreEqual(L"bar.txt", originalNameValue.c_str());
            Assert::AreEqual(L"", newNameValue.c_str());

            Assert::AreEqual(S_OK, mgr->Shutdown());
            mockMgrEvents->Release();
        }

        TEST_METHOD (VerifyKeepUiOpenRenameDoesNotCommitFailedItemState)
        {
            CTestFileHelper testFileHelper;
            Assert::IsTrue(testFileHelper.AddFile(L"foo1.txt"));
            Assert::IsTrue(testFileHelper.AddFile(L"foo2.txt"));

            CMockPowerRenameManagerEvents* mockMgrEvents = nullptr;
            CComPtr<IPowerRenameManager> mgr = CreateManagerWithEvents(&mockMgrEvents);

            CComPtr<IPowerRenameItem> item1;
            CComPtr<IPowerRenameItem> item2;
            CMockPowerRenameItem::CreateInstance(testFileHelper.GetFullPath(L"foo1.txt").c_str(), L"foo1.txt", 0, false, SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, &item1);
            CMockPowerRenameItem::CreateInstance(testFileHelper.GetFullPath(L"foo2.txt").c_str(), L"foo2.txt", 0, false, SYSTEMTIME{ 2020, 7, 3, 22, 15, 6, 42, 453 }, &item2);
            mgr->AddItem(item1);
            mgr->AddItem(item2);

            ConfigureRenameRegex(mgr, L"foo[0-9]", L"bar", DEFAULT_FLAGS | UseRegularExpressions);

            Assert::IsTrue(RenameWithRetries(mgr, false));

            int committedItems = 0;
            int notCommittedItems = 0;
            for (UINT index = 0; index < 2; index++)
            {
                CComPtr<IPowerRenameItem> currentItem;
                Assert::AreEqual(S_OK, mgr->GetItemByIndex(index, &currentItem));

                const auto pathValue = GetItemStringValue(currentItem, &IPowerRenameItem::GetPath);
                const auto originalNameValue = GetItemStringValue(currentItem, &IPowerRenameItem::GetOriginalName);
                const auto newNameValue = GetItemStringValue(currentItem, &IPowerRenameItem::GetNewName);

                if (originalNameValue == L"bar.txt")
                {
                    committedItems++;
                    Assert::AreEqual(testFileHelper.GetFullPath(L"bar.txt").c_str(), pathValue.c_str());
                    Assert::AreEqual(L"", newNameValue.c_str());
                }
                else
                {
                    notCommittedItems++;
                    const bool oldNameMatch = (originalNameValue == L"foo1.txt") || (originalNameValue == L"foo2.txt");
                    const bool oldPathMatch = (pathValue == testFileHelper.GetFullPath(L"foo1.txt")) ||
                                              (pathValue == testFileHelper.GetFullPath(L"foo2.txt"));
                    Assert::IsTrue(oldNameMatch);
                    Assert::IsTrue(oldPathMatch);
                    Assert::AreEqual(L"bar.txt", newNameValue.c_str());
                }
            }

            Assert::AreEqual(1, committedItems);
            Assert::AreEqual(1, notCommittedItems);
            Assert::IsTrue(testFileHelper.PathExistsCaseSensitive(L"bar.txt"));
            Assert::IsTrue(testFileHelper.PathExistsCaseSensitive(L"foo1.txt") || testFileHelper.PathExistsCaseSensitive(L"foo2.txt"));

            Assert::IsTrue(RenameWithRetries(mgr, false));

            committedItems = 0;
            notCommittedItems = 0;
            for (UINT index = 0; index < 2; index++)
            {
                CComPtr<IPowerRenameItem> currentItem;
                Assert::AreEqual(S_OK, mgr->GetItemByIndex(index, &currentItem));
                const auto originalNameValue = GetItemStringValue(currentItem, &IPowerRenameItem::GetOriginalName);
                const auto newNameValue = GetItemStringValue(currentItem, &IPowerRenameItem::GetNewName);
                if (originalNameValue == L"bar.txt")
                {
                    committedItems++;
                    Assert::AreEqual(L"", newNameValue.c_str());
                }
                else
                {
                    notCommittedItems++;
                    Assert::AreEqual(L"bar.txt", newNameValue.c_str());
                }
            }

            Assert::AreEqual(1, committedItems);
            Assert::AreEqual(1, notCommittedItems);

            Assert::AreEqual(S_OK, mgr->Shutdown());
            mockMgrEvents->Release();
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
            std::locale::global(std::locale(""));
            SYSTEMTIME fileTime = { 2020, 1, 3, 1, 15, 6, 42, 453 };
            wchar_t localeName[LOCALE_NAME_MAX_LENGTH];
            wchar_t result[MAX_PATH] = L"bar";
            wchar_t formattedDate[MAX_PATH];
            if (GetUserDefaultLocaleName(localeName, LOCALE_NAME_MAX_LENGTH) == 0)
                StringCchCopy(localeName, LOCALE_NAME_MAX_LENGTH, L"en_US");

            GetDateFormatEx(localeName, NULL, &fileTime, L"MMM", formattedDate, MAX_PATH, NULL);
            formattedDate[0] = towupper(formattedDate[0]);
            StringCchPrintf(result, MAX_PATH, TEXT("%s%s"), result, formattedDate);

            GetDateFormatEx(localeName, NULL, &fileTime, L"MMMM", formattedDate, MAX_PATH, NULL);
            formattedDate[0] = towupper(formattedDate[0]);
            StringCchPrintf(result, MAX_PATH, TEXT("%s-%s"), result, formattedDate);

            GetDateFormatEx(localeName, NULL, &fileTime, L"ddd", formattedDate, MAX_PATH, NULL);
            formattedDate[0] = towupper(formattedDate[0]);
            StringCchPrintf(result, MAX_PATH, TEXT("%s-%s"), result, formattedDate);

            GetDateFormatEx(localeName, NULL, &fileTime, L"dddd", formattedDate, MAX_PATH, NULL);
            formattedDate[0] = towupper(formattedDate[0]);
            StringCchPrintf(result, MAX_PATH, TEXT("%s-%s"), result, formattedDate);

            rename_pairs renamePairs[] = {
                { L"foo", result, true, true, 0 },
            };

            RenameHelper(renamePairs, ARRAYSIZE(renamePairs), L"foo", L"bar$MMM-$MMMM-$DDD-$DDDD", SYSTEMTIME{ 2020, 1, 3, 1, 15, 6, 42, 453 }, DEFAULT_FLAGS);
        }
    };
}
