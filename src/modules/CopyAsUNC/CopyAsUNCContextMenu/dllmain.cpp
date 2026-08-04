// dllmain.cpp : Defines the entry point for the DLL application.
#include "pch.h"

#include <common/utils/process_path.h>
#include <common/utils/resources.h>

#include "CopyAsUNCLib/PathConversion.h"
#include "CopyAsUNCLib/Settings.h"

#include <Shlwapi.h>
#include <shobjidl_core.h>
#include <winnetwk.h>
#include <string>
#include <vector>
#include <wil/resource.h>
#include <wrl/module.h>

#include "Generated Files/resource.h"

#pragma comment(lib, "Mpr.lib")

using namespace Microsoft::WRL;

HINSTANCE g_hInst = 0;

namespace
{
    std::vector<std::wstring> GetSelectedPaths(IShellItemArray* selection)
    {
        std::vector<std::wstring> paths;
        if (!selection)
        {
            return paths;
        }

        DWORD itemCount = 0;
        if (FAILED(selection->GetCount(&itemCount)))
        {
            return paths;
        }

        paths.reserve(itemCount);
        for (DWORD itemIndex = 0; itemIndex < itemCount; ++itemIndex)
        {
            ComPtr<IShellItem> item;
            if (FAILED(selection->GetItemAt(itemIndex, item.GetAddressOf())))
            {
                continue;
            }

            wil::unique_cotaskmem_string filePath;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &filePath)))
            {
                paths.emplace_back(filePath.get());
            }
        }

        return paths;
    }
}

BOOL APIENTRY DllMain(HMODULE hModule,
                      DWORD ul_reason_for_call,
                      LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        g_hInst = hModule;
        break;
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

class __declspec(uuid("89A22F51-9ED6-48FE-81FE-5DFD36F8CD32")) CopyAsUNCContextMenuCommand final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand, IObjectWithSite>
{
public:
    virtual const wchar_t* Title() { return L"Copy as UNC path"; }
    virtual const EXPCMDFLAGS Flags() { return ECF_DEFAULT; }
    virtual const EXPCMDSTATE State(_In_opt_ IShellItemArray*) { return ECS_ENABLED; }

    // IExplorerCommand
    IFACEMETHODIMP GetTitle(_In_opt_ IShellItemArray*, _Outptr_result_nullonfailure_ PWSTR* name)
    {
        return SHStrDup(context_menu_caption.c_str(), name);
    }

    IFACEMETHODIMP GetIcon(_In_opt_ IShellItemArray*, _Outptr_result_nullonfailure_ PWSTR* icon)
    {
        std::wstring iconResourcePath = get_module_folderpath(g_hInst);
        iconResourcePath += L"\\Assets\\CopyAsUNC\\";
        iconResourcePath += L"CopyAsUNC.ico";
        return SHStrDup(iconResourcePath.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(_In_opt_ IShellItemArray*, _Outptr_result_nullonfailure_ PWSTR* infoTip)
    {
        *infoTip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(_Out_ GUID* guidCommandName)
    {
        *guidCommandName = __uuidof(this);
        return S_OK;
    }

    IFACEMETHODIMP GetState(_In_opt_ IShellItemArray* selection, _In_ BOOL okToBeSlow, _Out_ EXPCMDSTATE* cmdState)
    try
    {
        *cmdState = ECS_HIDDEN;

        if (!CopyAsUNCSettingsInstance().GetEnabled())
            return S_OK;

        if (!okToBeSlow)
            return E_PENDING;

        if (!copy_as_unc::BuildClipboardText(GetSelectedPaths(selection)).empty())
        {
            *cmdState = ECS_ENABLED;
        }

        return S_OK;
    }
    CATCH_RETURN();

    IFACEMETHODIMP Invoke(_In_opt_ IShellItemArray* selection, _In_opt_ IBindCtx*) noexcept
    try
    {
        if (!selection)
            return S_OK;

        const std::wstring clipboardText = copy_as_unc::BuildClipboardText(GetSelectedPaths(selection));

        if (clipboardText.empty())
            return S_OK;

        size_t byteLen = (clipboardText.size() + 1) * sizeof(wchar_t);
        wil::unique_hglobal clipboardData{ GlobalAlloc(GMEM_MOVEABLE, byteLen) };
        if (!clipboardData)
            return HRESULT_FROM_WIN32(GetLastError());

        void* locked = GlobalLock(clipboardData.get());
        if (!locked)
            return HRESULT_FROM_WIN32(GetLastError());

        memcpy(locked, clipboardText.c_str(), byteLen);
        GlobalUnlock(clipboardData.get());

        HWND clipboardOwner = nullptr;
        if (!m_site || FAILED(IUnknown_GetWindow(m_site.Get(), &clipboardOwner)) || !clipboardOwner)
            return E_FAIL;

        if (!OpenClipboard(clipboardOwner))
            return HRESULT_FROM_WIN32(GetLastError());

        if (!EmptyClipboard())
        {
            const HRESULT result = HRESULT_FROM_WIN32(GetLastError());
            CloseClipboard();
            return result;
        }

        if (!SetClipboardData(CF_UNICODETEXT, clipboardData.get()))
        {
            const HRESULT result = HRESULT_FROM_WIN32(GetLastError());
            CloseClipboard();
            return result;
        }

        clipboardData.release();
        CloseClipboard();
        return S_OK;
    }
    CATCH_RETURN();

    IFACEMETHODIMP GetFlags(_Out_ EXPCMDFLAGS* flags)
    {
        *flags = Flags();
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(_COM_Outptr_ IEnumExplorerCommand** enumCommands)
    {
        *enumCommands = nullptr;
        return E_NOTIMPL;
    }

    // IObjectWithSite
    IFACEMETHODIMP SetSite(_In_ IUnknown* site) noexcept
    {
        m_site = site;
        return S_OK;
    }
    IFACEMETHODIMP GetSite(_In_ REFIID riid, _COM_Outptr_ void** site) noexcept { return m_site.CopyTo(riid, site); }

protected:
    ComPtr<IUnknown> m_site;
    std::wstring context_menu_caption = GET_RESOURCE_STRING_FALLBACK(IDS_COPY_AS_UNC_CONTEXT_MENU_ENTRY, L"Copy as UNC path");
};

CoCreatableClass(CopyAsUNCContextMenuCommand)
    CoCreatableClassWrlCreatorMapInclude(CopyAsUNCContextMenuCommand)

        STDAPI DllGetActivationFactory(_In_ HSTRING activatableClassId, _COM_Outptr_ IActivationFactory** factory)
{
    return Module<ModuleType::InProc>::GetModule().GetActivationFactory(activatableClassId, factory);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _COM_Outptr_ void** instance)
{
    return Module<InProc>::GetModule().GetClassObject(rclsid, riid, instance);
}
