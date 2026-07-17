#include "pch.h"

#include "shell_context_menu.h"
#include "dll_main.h"
#include "trace.h"

#include <common/Telemetry/EtwTrace/EtwTrace.h>
#include <atomic>

HMODULE module_instance_handle = 0;
Shared::Trace::ETWTrace trace(L"NewPlusShellExtension");
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

    // Re-check module count to reduce race window against concurrent COM object activity.
    return module.GetObjectCount() == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _Outptr_ LPVOID FAR* ppv)
{
    return Module<InProc>::GetModule().GetClassObject(rclsid, riid, ppv);
}

CoCreatableClass(shell_context_menu)

void increment_background_worker_count()
{
    active_background_workers.fetch_add(1, std::memory_order_acq_rel);
}

void decrement_background_worker_count()
{
    active_background_workers.fetch_sub(1, std::memory_order_acq_rel);
}
