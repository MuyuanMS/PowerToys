// Copyright (c) Microsoft Corporation
// Licensed under the MIT license.

#include "FileGuard.h"

#include <windows.h>
#include <sddl.h>
#include <aclapi.h>
#include <pathcch.h>
#include <memory>
#include <vector>
#include <algorithm>
#include <filesystem>

#pragma comment(lib, "Advapi32.lib")
#pragma comment(lib, "Pathcch.lib")

namespace PTSettingsSvc
{
    namespace
    {
        struct LocalFreeDeleter
        {
            void operator()(void* p) const noexcept { if (p) LocalFree(p); }
        };

        // Enables a privilege (e.g. SeRestore/SeTakeOwnership) on the current
        // process token so the register path can set the store owner to SYSTEM.
        // The elevated registrar (SYSTEM CA, or elevated admin provisioner) holds
        // these privileges; they are merely disabled by default.
        void EnablePrivilege(const wchar_t* name)
        {
            HANDLE token = nullptr;
            if (!OpenProcessToken(GetCurrentProcess(),
                                  TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &token))
            {
                return;
            }
            LUID luid{};
            if (LookupPrivilegeValueW(nullptr, name, &luid))
            {
                TOKEN_PRIVILEGES tp{};
                tp.PrivilegeCount = 1;
                tp.Privileges[0].Luid = luid;
                tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
                AdjustTokenPrivileges(token, FALSE, &tp, sizeof(tp), nullptr, nullptr);
            }
            CloseHandle(token);
        }

        HRESULT EnsureAndOpenRealDirectory(const std::wstring& path,
                                           DWORD desiredAccess,
                                           HANDLE& outHandle)
        {
            outHandle = INVALID_HANDLE_VALUE;
            if (!CreateDirectoryW(path.c_str(), nullptr))
            {
                DWORD err = GetLastError();
                if (err != ERROR_ALREADY_EXISTS)
                {
                    return HRESULT_FROM_WIN32(err);
                }
            }

            HANDLE directory = CreateFileW(
                path.c_str(),
                desiredAccess,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr);
            if (directory == INVALID_HANDLE_VALUE)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }

            FILE_ATTRIBUTE_TAG_INFO attributes{};
            if (!GetFileInformationByHandleEx(
                    directory,
                    FileAttributeTagInfo,
                    &attributes,
                    sizeof(attributes)))
            {
                DWORD err = GetLastError();
                CloseHandle(directory);
                return HRESULT_FROM_WIN32(err);
            }

            if ((attributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0 ||
                (attributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                CloseHandle(directory);
                return HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
            }

            outHandle = directory;
            return S_OK;
        }

        HRESULT OpenRealFile(const std::wstring& path,
                             DWORD desiredAccess,
                             HANDLE& outHandle)
        {
            outHandle = CreateFileW(
                path.c_str(),
                desiredAccess,
                FILE_SHARE_READ,
                nullptr,
                OPEN_EXISTING,
                FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr);
            if (outHandle == INVALID_HANDLE_VALUE)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }

            FILE_ATTRIBUTE_TAG_INFO attributes{};
            const bool gotAttributes = GetFileInformationByHandleEx(
                                           outHandle,
                                           FileAttributeTagInfo,
                                           &attributes,
                                           sizeof(attributes)) != FALSE;
            if (!gotAttributes ||
                (attributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ||
                (attributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                DWORD err = gotAttributes ? ERROR_ACCESS_DENIED : GetLastError();
                CloseHandle(outHandle);
                outHandle = INVALID_HANDLE_VALUE;
                return HRESULT_FROM_WIN32(err);
            }

            BY_HANDLE_FILE_INFORMATION fileInfo{};
            const bool gotFileInfo =
                GetFileInformationByHandle(outHandle, &fileInfo) != FALSE;
            if (!gotFileInfo ||
                fileInfo.nNumberOfLinks != 1)
            {
                DWORD err = gotFileInfo ? ERROR_ACCESS_DENIED : GetLastError();
                CloseHandle(outHandle);
                outHandle = INVALID_HANDLE_VALUE;
                return HRESULT_FROM_WIN32(err);
            }

            return S_OK;
        }

        // Applies the PROTECTED per-user DACL and sets owner = SYSTEM.
        //   serviceAccountName = the virtual account, e.g.
        //   L"NT SERVICE\\PTSettingsSvc_<SID>" (Full Control writer).
        HRESULT ApplyProtectiveDacl(const std::wstring& target,
                                    const std::wstring& userSidString,
                                    const std::wstring& serviceAccountName,
                                    bool isDirectory = true)
        {
            PSID userSid = nullptr;
            if (!ConvertStringSidToSidW(userSidString.c_str(), &userSid))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            std::unique_ptr<void, LocalFreeDeleter> userSidGuard(userSid);

            PSID adminSid = nullptr;
            if (!ConvertStringSidToSidW(L"S-1-5-32-544", &adminSid)) // BUILTIN\Administrators
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            std::unique_ptr<void, LocalFreeDeleter> adminSidGuard(adminSid);

            PSID systemSid = nullptr;
            if (!ConvertStringSidToSidW(L"S-1-5-18", &systemSid))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            std::unique_ptr<void, LocalFreeDeleter> systemGuard(systemSid);

            // The per-user folder DACL is:
            //   svc-vaccount:F, admin:F, SYSTEM:F, <specific user>:RX
            // Owner = SYSTEM so the low-privilege virtual account cannot rewrite
            // the DACL.  PROTECTED below blocks inheritance from the store root
            // (its blanket AuthUsers:RX does NOT carry through here — that's how
            // user A can't read user B's data).  The virtual account is named
            // (TRUSTEE_IS_NAME) because it exists only after CreateService.
            std::wstring svcAccount = serviceAccountName;
            EXPLICIT_ACCESS_W ea[4] = {};

            ea[0].grfAccessPermissions = GENERIC_ALL;
            ea[0].grfAccessMode = SET_ACCESS;
            const DWORD inheritance =
                isDirectory ? SUB_CONTAINERS_AND_OBJECTS_INHERIT : NO_INHERITANCE;

            ea[0].grfInheritance = inheritance;
            ea[0].Trustee.TrusteeForm = TRUSTEE_IS_NAME;
            ea[0].Trustee.TrusteeType = TRUSTEE_IS_USER;
            ea[0].Trustee.ptstrName = svcAccount.data();

            ea[1].grfAccessPermissions = GENERIC_ALL;
            ea[1].grfAccessMode = SET_ACCESS;
            ea[1].grfInheritance = inheritance;
            ea[1].Trustee.TrusteeForm = TRUSTEE_IS_SID;
            ea[1].Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
            ea[1].Trustee.ptstrName = static_cast<LPWSTR>(adminSid);

            ea[2].grfAccessPermissions = GENERIC_ALL;
            ea[2].grfAccessMode = SET_ACCESS;
            ea[2].grfInheritance = inheritance;
            ea[2].Trustee.TrusteeForm = TRUSTEE_IS_SID;
            ea[2].Trustee.TrusteeType = TRUSTEE_IS_USER;
            ea[2].Trustee.ptstrName = static_cast<LPWSTR>(systemSid);

            ea[3].grfAccessPermissions = GENERIC_READ | GENERIC_EXECUTE;
            ea[3].grfAccessMode = SET_ACCESS;
            ea[3].grfInheritance = inheritance;
            ea[3].Trustee.TrusteeForm = TRUSTEE_IS_SID;
            ea[3].Trustee.TrusteeType = TRUSTEE_IS_USER;
            ea[3].Trustee.ptstrName = static_cast<LPWSTR>(userSid);

            PACL acl = nullptr;
            DWORD rc = SetEntriesInAclW(ARRAYSIZE(ea), ea, nullptr, &acl);
            if (rc != ERROR_SUCCESS)
            {
                return HRESULT_FROM_WIN32(rc);
            }
            std::unique_ptr<void, LocalFreeDeleter> aclGuard(acl);

            EnablePrivilege(SE_RESTORE_NAME);
            EnablePrivilege(SE_TAKE_OWNERSHIP_NAME);

            HANDLE targetHandle = INVALID_HANDLE_VALUE;
            HRESULT hr = isDirectory
                             ? EnsureAndOpenRealDirectory(
                                   target,
                                   READ_CONTROL | WRITE_DAC | WRITE_OWNER,
                                   targetHandle)
                             : OpenRealFile(
                                   target,
                                   READ_CONTROL | WRITE_DAC | WRITE_OWNER,
                                   targetHandle);
            if (FAILED(hr))
            {
                return hr;
            }

            rc = SetSecurityInfo(targetHandle,
                                 SE_FILE_OBJECT,
                                 OWNER_SECURITY_INFORMATION |
                                     DACL_SECURITY_INFORMATION |
                                     PROTECTED_DACL_SECURITY_INFORMATION,
                                 systemSid, nullptr, acl, nullptr);
            CloseHandle(targetHandle);
            return rc == ERROR_SUCCESS ? S_OK : HRESULT_FROM_WIN32(rc);
        }

        bool FileOwnerIsTrusted(HANDLE file,
                                const std::wstring& serviceAccountName)
        {
            PSID owner = nullptr;
            PSECURITY_DESCRIPTOR descriptor = nullptr;
            if (GetSecurityInfo(
                    file,
                    SE_FILE_OBJECT,
                    OWNER_SECURITY_INFORMATION,
                    &owner,
                    nullptr,
                    nullptr,
                    nullptr,
                    &descriptor) != ERROR_SUCCESS)
            {
                return false;
            }
            std::unique_ptr<void, LocalFreeDeleter> descriptorGuard(descriptor);
            if (!owner)
            {
                return false;
            }

            PSID systemSid = nullptr;
            if (!ConvertStringSidToSidW(L"S-1-5-18", &systemSid))
            {
                return false;
            }
            std::unique_ptr<void, LocalFreeDeleter> systemGuard(systemSid);

            DWORD sidSize = 0;
            DWORD domainSize = 0;
            SID_NAME_USE use{};
            LookupAccountNameW(
                nullptr,
                serviceAccountName.c_str(),
                nullptr,
                &sidSize,
                nullptr,
                &domainSize,
                &use);
            std::vector<BYTE> serviceSid(sidSize);
            std::vector<wchar_t> domain(domainSize);
            const bool haveServiceSid =
                sidSize > 0 &&
                LookupAccountNameW(
                    nullptr,
                    serviceAccountName.c_str(),
                    serviceSid.data(),
                    &sidSize,
                    domain.data(),
                    &domainSize,
                    &use);

            return EqualSid(owner, systemSid) ||
                   (haveServiceSid && EqualSid(owner, serviceSid.data()));
        }
    }

    HRESULT EnsureStoreRoot(const std::wstring& root)
    {
        HRESULT hierarchyHr = EnsureDirectoryHierarchyNoReparse(root);
        if (FAILED(hierarchyHr))
        {
            return hierarchyHr;
        }

        PSID adminSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-32-544", &adminSid))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> adminGuard(adminSid);

        PSID systemSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-18", &systemSid))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> systemGuard(systemSid);

        PSID authUsersSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-11", &authUsersSid)) // Authenticated Users
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> authUsersGuard(authUsersSid);

        // Root: SYSTEM/Admins Full, Authenticated Users RX (traverse only).
        // Protect the DACL so inherited ProgramData ACEs cannot reintroduce
        // create/write rights at this security boundary.
        EXPLICIT_ACCESS_W ea[3] = {};
        ea[0].grfAccessPermissions = GENERIC_ALL;
        ea[0].grfAccessMode = SET_ACCESS;
        ea[0].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[0].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[0].Trustee.TrusteeType = TRUSTEE_IS_USER;
        ea[0].Trustee.ptstrName = static_cast<LPWSTR>(systemSid);

        ea[1].grfAccessPermissions = GENERIC_ALL;
        ea[1].grfAccessMode = SET_ACCESS;
        ea[1].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[1].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[1].Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
        ea[1].Trustee.ptstrName = static_cast<LPWSTR>(adminSid);

        ea[2].grfAccessPermissions = GENERIC_READ | GENERIC_EXECUTE;
        ea[2].grfAccessMode = SET_ACCESS;
        ea[2].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[2].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[2].Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
        ea[2].Trustee.ptstrName = static_cast<LPWSTR>(authUsersSid);

        PACL acl = nullptr;
        DWORD rc = SetEntriesInAclW(ARRAYSIZE(ea), ea, nullptr, &acl);
        if (rc != ERROR_SUCCESS)
        {
            return HRESULT_FROM_WIN32(rc);
        }
        std::unique_ptr<void, LocalFreeDeleter> aclGuard(acl);

        EnablePrivilege(SE_RESTORE_NAME);
        EnablePrivilege(SE_TAKE_OWNERSHIP_NAME);

        HANDLE directory = INVALID_HANDLE_VALUE;
        HRESULT hr = EnsureAndOpenRealDirectory(
            root,
            READ_CONTROL | WRITE_DAC | WRITE_OWNER,
            directory);
        if (FAILED(hr))
        {
            return hr;
        }

        rc = SetSecurityInfo(directory,
                             SE_FILE_OBJECT,
                             OWNER_SECURITY_INFORMATION |
                                 DACL_SECURITY_INFORMATION |
                                 PROTECTED_DACL_SECURITY_INFORMATION,
                             systemSid, nullptr, acl, nullptr);
        CloseHandle(directory);
        return rc == ERROR_SUCCESS ? S_OK : HRESULT_FROM_WIN32(rc);
    }

    HRESULT HardenStagingDirAdminOnly(const std::wstring& dir)
    {
        PSID adminSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-32-544", &adminSid)) // BUILTIN\Administrators
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> adminGuard(adminSid);

        PSID systemSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-18", &systemSid))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> systemGuard(systemSid);

        // SYSTEM Full + Administrators Full ONLY.  No Authenticated-Users ACE and
        // — combined with PROTECTED below — no inherited %ProgramData% ACEs, so a
        // non-admin who pre-created this dir keeps nothing.  Owner is reset to
        // SYSTEM so a non-admin creator's CREATOR-OWNER rights are reclaimed.
        // The virtual account / Users RX ACEs are added later by
        // ProtectServiceBinDir, once the service (hence the account) exists.
        EXPLICIT_ACCESS_W ea[2] = {};
        ea[0].grfAccessPermissions = GENERIC_ALL;
        ea[0].grfAccessMode = SET_ACCESS;
        ea[0].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[0].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[0].Trustee.TrusteeType = TRUSTEE_IS_USER;
        ea[0].Trustee.ptstrName = static_cast<LPWSTR>(systemSid);

        ea[1].grfAccessPermissions = GENERIC_ALL;
        ea[1].grfAccessMode = SET_ACCESS;
        ea[1].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[1].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[1].Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
        ea[1].Trustee.ptstrName = static_cast<LPWSTR>(adminSid);

        PACL acl = nullptr;
        DWORD rc = SetEntriesInAclW(ARRAYSIZE(ea), ea, nullptr, &acl);
        if (rc != ERROR_SUCCESS)
        {
            return HRESULT_FROM_WIN32(rc);
        }
        std::unique_ptr<void, LocalFreeDeleter> aclGuard(acl);

        EnablePrivilege(SE_RESTORE_NAME);
        EnablePrivilege(SE_TAKE_OWNERSHIP_NAME);

        HANDLE directory = INVALID_HANDLE_VALUE;
        HRESULT hr = EnsureAndOpenRealDirectory(
            dir,
            READ_CONTROL | WRITE_DAC | WRITE_OWNER,
            directory);
        if (FAILED(hr))
        {
            return hr;
        }

        rc = SetSecurityInfo(directory,
                             SE_FILE_OBJECT,
                             OWNER_SECURITY_INFORMATION |
                                 DACL_SECURITY_INFORMATION |
                                 PROTECTED_DACL_SECURITY_INFORMATION,
                             systemSid, nullptr, acl, nullptr);
        CloseHandle(directory);
        return rc == ERROR_SUCCESS ? S_OK : HRESULT_FROM_WIN32(rc);
    }

    HRESULT EnsureUserFolder(const std::wstring& folder,
                             const std::wstring& userSidString,
                             const std::wstring& serviceAccountName)
    {
        return ApplyProtectiveDacl(folder, userSidString, serviceAccountName);
    }

    HRESULT ProvisionStore(const std::wstring& root,
                           const std::wstring& userFolder,
                           const std::wstring& userSidString,
                           const std::wstring& serviceAccountName)
    {
        HRESULT hr = EnsureStoreRoot(root);
        if (FAILED(hr))
        {
            return hr;
        }
        return EnsureUserFolder(userFolder, userSidString, serviceAccountName);
    }

    HRESULT ProtectServiceBinDir(const std::wstring& binDir,
                                 const std::wstring& serviceAccountName)
    {
        PSID adminSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-32-544", &adminSid))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> adminGuard(adminSid);

        PSID systemSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-18", &systemSid))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> systemGuard(systemSid);

        PSID usersSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-32-545", &usersSid)) // BUILTIN\Users
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> usersGuard(usersSid);

        PSID allServicesSid = nullptr;
        if (!ConvertStringSidToSidW(L"S-1-5-80-0", &allServicesSid)) // NT SERVICE\ALL SERVICES
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        std::unique_ptr<void, LocalFreeDeleter> allServicesGuard(allServicesSid);

        std::wstring svcAccount = serviceAccountName;
        EXPLICIT_ACCESS_W ea[5] = {};

        ea[0].grfAccessPermissions = GENERIC_ALL;
        ea[0].grfAccessMode = SET_ACCESS;
        ea[0].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[0].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[0].Trustee.TrusteeType = TRUSTEE_IS_USER;
        ea[0].Trustee.ptstrName = static_cast<LPWSTR>(systemSid);

        ea[1].grfAccessPermissions = GENERIC_ALL;
        ea[1].grfAccessMode = SET_ACCESS;
        ea[1].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[1].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[1].Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
        ea[1].Trustee.ptstrName = static_cast<LPWSTR>(adminSid);

        ea[2].grfAccessPermissions = GENERIC_READ | GENERIC_EXECUTE;
        ea[2].grfAccessMode = SET_ACCESS;
        ea[2].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[2].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[2].Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
        ea[2].Trustee.ptstrName = static_cast<LPWSTR>(usersSid);

        ea[3].grfAccessPermissions = GENERIC_READ | GENERIC_EXECUTE;
        ea[3].grfAccessMode = SET_ACCESS;
        ea[3].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[3].Trustee.TrusteeForm = TRUSTEE_IS_NAME;
        ea[3].Trustee.TrusteeType = TRUSTEE_IS_USER;
        ea[3].Trustee.ptstrName = svcAccount.data();

        ea[4].grfAccessPermissions = GENERIC_READ | GENERIC_EXECUTE;
        ea[4].grfAccessMode = SET_ACCESS;
        ea[4].grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        ea[4].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[4].Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
        ea[4].Trustee.ptstrName = static_cast<LPWSTR>(allServicesSid);

        PACL acl = nullptr;
        DWORD rc = SetEntriesInAclW(ARRAYSIZE(ea), ea, nullptr, &acl);
        if (rc != ERROR_SUCCESS)
        {
            return HRESULT_FROM_WIN32(rc);
        }
        std::unique_ptr<void, LocalFreeDeleter> aclGuard(acl);

        EnablePrivilege(SE_RESTORE_NAME);
        EnablePrivilege(SE_TAKE_OWNERSHIP_NAME);

        HANDLE directory = INVALID_HANDLE_VALUE;
        HRESULT hr = EnsureAndOpenRealDirectory(
            binDir,
            READ_CONTROL | WRITE_DAC | WRITE_OWNER,
            directory);
        if (FAILED(hr))
        {
            return hr;
        }

        rc = SetSecurityInfo(directory,
                             SE_FILE_OBJECT,
                             OWNER_SECURITY_INFORMATION |
                                 DACL_SECURITY_INFORMATION |
                                 PROTECTED_DACL_SECURITY_INFORMATION,
                             systemSid, nullptr, acl, nullptr);
        CloseHandle(directory);
        return rc == ERROR_SUCCESS ? S_OK : HRESULT_FROM_WIN32(rc);
    }

    HRESULT EnsureDirectory(const std::wstring& dir)
    {
        HANDLE directory = INVALID_HANDLE_VALUE;
        HRESULT hr = EnsureAndOpenRealDirectory(
            dir,
            FILE_READ_ATTRIBUTES,
            directory);
        if (SUCCEEDED(hr))
        {
            CloseHandle(directory);
        }
        return hr;
    }

    HRESULT EnsureDirectoryHierarchyNoReparse(const std::wstring& dir)
    {
        std::vector<std::filesystem::path> components;
        std::filesystem::path current(dir);
        const std::filesystem::path root = current.root_path();
        while (!current.empty() && current != root)
        {
            components.push_back(current);
            current = current.parent_path();
        }
        std::reverse(components.begin(), components.end());

        for (const auto& component : components)
        {
            HANDLE directory = INVALID_HANDLE_VALUE;
            HRESULT hr = EnsureAndOpenRealDirectory(
                component.wstring(),
                FILE_READ_ATTRIBUTES,
                directory);
            if (FAILED(hr))
            {
                return hr;
            }
            CloseHandle(directory);
        }
        return S_OK;
    }

    HRESULT SanitizeNamespaceFiles(const std::wstring& namespaceFolder,
                                   const std::wstring& fileName,
                                   const std::wstring& userSidString,
                                   const std::wstring& serviceAccountName)
    {
        const std::wstring dataPath = namespaceFolder + L"\\" + fileName;
        const std::wstring tempPath = dataPath + L".tmp";

        DWORD tempAttributes = GetFileAttributesW(tempPath.c_str());
        if (tempAttributes != INVALID_FILE_ATTRIBUTES)
        {
            if ((tempAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
            {
                return HRESULT_FROM_WIN32(ERROR_DIRECTORY);
            }
            SetFileAttributesW(tempPath.c_str(), FILE_ATTRIBUTE_NORMAL);
            if (!DeleteFileW(tempPath.c_str()))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
        }

        DWORD dataAttributes = GetFileAttributesW(dataPath.c_str());
        if (dataAttributes == INVALID_FILE_ATTRIBUTES)
        {
            DWORD err = GetLastError();
            return err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND
                       ? S_OK
                       : HRESULT_FROM_WIN32(err);
        }
        if ((dataAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            return HRESULT_FROM_WIN32(ERROR_DIRECTORY);
        }

        EnablePrivilege(SE_RESTORE_NAME);
        EnablePrivilege(SE_TAKE_OWNERSHIP_NAME);

        HANDLE file = INVALID_HANDLE_VALUE;
        HRESULT hr = OpenRealFile(dataPath, READ_CONTROL, file);
        if (FAILED(hr) || !FileOwnerIsTrusted(file, serviceAccountName))
        {
            if (file != INVALID_HANDLE_VALUE)
            {
                CloseHandle(file);
            }
            SetFileAttributesW(dataPath.c_str(), FILE_ATTRIBUTE_NORMAL);
            if (!DeleteFileW(dataPath.c_str()))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            return S_OK;
        }
        CloseHandle(file);

        return ApplyProtectiveDacl(
            dataPath,
            userSidString,
            serviceAccountName,
            false);
    }

    HRESULT WriteFileAtomically(const std::wstring& targetFile,
                                const std::vector<BYTE>& bytes)
    {
        std::wstring tmp = targetFile + L".tmp";

        DWORD tempAttributes = GetFileAttributesW(tmp.c_str());
        if (tempAttributes != INVALID_FILE_ATTRIBUTES)
        {
            if ((tempAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
            {
                return HRESULT_FROM_WIN32(ERROR_DIRECTORY);
            }
            SetFileAttributesW(tmp.c_str(), FILE_ATTRIBUTE_NORMAL);
            if (!DeleteFileW(tmp.c_str()))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
        }

        HANDLE h = CreateFileW(tmp.c_str(),
                               GENERIC_WRITE,
                               0,
                               nullptr,
                               CREATE_NEW,
                               FILE_ATTRIBUTE_NORMAL,
                               nullptr);
        if (h == INVALID_HANDLE_VALUE)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        DWORD written = 0;
        BOOL ok = WriteFile(h,
                            bytes.data(),
                            static_cast<DWORD>(bytes.size()),
                            &written,
                            nullptr);
        DWORD writeErr = ok ? ERROR_SUCCESS : GetLastError();
        const BOOL flushed = FlushFileBuffers(h);
        const DWORD flushError = flushed ? ERROR_SUCCESS : GetLastError();
        CloseHandle(h);

        if (!ok || written != bytes.size() || !flushed)
        {
            DeleteFileW(tmp.c_str());
            const DWORD error =
                writeErr ? writeErr :
                flushError ? flushError :
                ERROR_WRITE_FAULT;
            return HRESULT_FROM_WIN32(error);
        }

        if (!ReplaceFileW(targetFile.c_str(),
                          tmp.c_str(),
                          nullptr,
                          REPLACEFILE_WRITE_THROUGH | REPLACEFILE_IGNORE_MERGE_ERRORS,
                          nullptr,
                          nullptr))
        {
            DWORD err = GetLastError();
            if (err == ERROR_FILE_NOT_FOUND)
            {
                // No existing file — MoveFile is sufficient.
                if (!MoveFileExW(tmp.c_str(),
                                 targetFile.c_str(),
                                 MOVEFILE_WRITE_THROUGH))
                {
                    DWORD mvErr = GetLastError();
                    DeleteFileW(tmp.c_str());
                    return HRESULT_FROM_WIN32(mvErr);
                }
            }
            else
            {
                DeleteFileW(tmp.c_str());
                return HRESULT_FROM_WIN32(err);
            }
        }

        return S_OK;
    }

    HRESULT ReadFileFully(const std::wstring& path,
                          uint32_t maxBytes,
                          std::vector<BYTE>& outBytes)
    {
        outBytes.clear();

        HANDLE h = CreateFileW(path.c_str(),
                               GENERIC_READ,
                               FILE_SHARE_READ,
                               nullptr,
                               OPEN_EXISTING,
                               FILE_ATTRIBUTE_NORMAL,
                               nullptr);
        if (h == INVALID_HANDLE_VALUE)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        LARGE_INTEGER size{};
        if (!GetFileSizeEx(h, &size))
        {
            DWORD err = GetLastError();
            CloseHandle(h);
            return HRESULT_FROM_WIN32(err);
        }
        if (size.QuadPart > static_cast<LONGLONG>(maxBytes))
        {
            CloseHandle(h);
            return HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE);
        }

        outBytes.resize(static_cast<size_t>(size.QuadPart));
        DWORD read = 0;
        BOOL ok = ReadFile(h,
                           outBytes.data(),
                           static_cast<DWORD>(outBytes.size()),
                           &read,
                           nullptr);
        DWORD err = ok ? ERROR_SUCCESS : GetLastError();
        CloseHandle(h);

        if (!ok || read != outBytes.size())
        {
            outBytes.clear();
            return HRESULT_FROM_WIN32(err ? err : ERROR_READ_FAULT);
        }
        return S_OK;
    }
}
