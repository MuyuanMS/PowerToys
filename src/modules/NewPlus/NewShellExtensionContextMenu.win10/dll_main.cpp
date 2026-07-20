#include "pch.h"

#include "shell_context_menu_win10.h"
#include "dll_main.h"
#include "trace.h"

#include <common/Telemetry/EtwTrace/EtwTrace.h>
#include <atomic>
#include <stdexcept>

HMODULE module_instance_handle = 0;
Shared::Trace::ETWTrace trace(L"NewPlusShellExtension_Win10");
std::atomic<long> active_background_workers = 0;

BOOL APIENTRY DllMain(HMODULE module_handle, DWORD ul_reason_for_call, LPVOID reserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        module_instance_handle = module_handle;
        Trace::RegisterProvider();
        newplus::utilities::init_logger();
        break;

    case DLL_PROCESS_DETACH:
        Trace::UnregisterProvider();
        break;
    }
    return TRUE;
}

STDAPI DllGetActivationFactory(_In_ HSTRING activatableClassId, _COM_Outptr_ IActivationFactory** factory)
{
    return Module<ModuleType::InProc>::GetModule().GetActivationFactory(activatableClassId, factory);
}

STDAPI DllCanUnloadNow()
{
    const auto& module = Module<InProc>::GetModule();
    if (module.GetObjectCount() != 0)
    {
        return S_FALSE;
    }

    if (active_background_workers.load(std::memory_order_acquire) != 0)
    {
        return S_FALSE;
    }

    return module.GetObjectCount() == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(_In_ REFCLSID ref_class_id, _In_ REFIID ref_interface_id, _Outptr_ LPVOID FAR* object)
{
    return Module<InProc>::GetModule().GetClassObject(ref_class_id, ref_interface_id, object);
}

CoCreatableClass(shell_context_menu_win10)

HMODULE acquire_background_worker_module_reference()
{
    HMODULE module_reference = nullptr;
    if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, reinterpret_cast<LPCWSTR>(&module_instance_handle), &module_reference) == FALSE)
    {
        throw std::runtime_error("Failed to acquire New+ shell extension module reference");
    }

    active_background_workers.fetch_add(1, std::memory_order_acq_rel);
    return module_reference;
}

void release_background_worker_module_reference(HMODULE module_reference)
{
    active_background_workers.fetch_sub(1, std::memory_order_acq_rel);
    if (module_reference != nullptr)
    {
        FreeLibrary(module_reference);
    }
}

[[noreturn]] void release_background_worker_module_reference_and_exit_thread(HMODULE module_reference)
{
    active_background_workers.fetch_sub(1, std::memory_order_acq_rel);
    FreeLibraryAndExitThread(module_reference, 0);
}
