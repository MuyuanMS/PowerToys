#pragma once
#include <common/hooks/LowlevelKeyboardEvent.h>
#include <common/utils/EventWaiter.h>
#include <keyboardmanager/common/Input.h>
#include <mutex>
#include "State.h"

class KeyboardManager
{
public:
    static const inline DWORD StartHookMessageID = WM_APP + 1;

    // Constructor
    KeyboardManager();

    ~KeyboardManager()
    {
        if (editorIsRunningEvent)
        {
            CloseHandle(editorIsRunningEvent);
        }
        if (hookThreadHandle)
        {
            CloseHandle(hookThreadHandle);
        }
    }

    void StartLowlevelKeyboardHook();
    void StopLowlevelKeyboardHook();

    bool HasRegisteredRemappings() const;

private:
    // Returns whether there are any remappings available without waiting for settings to load
    bool HasRegisteredRemappingsUnchecked() const;

    // Contains the non localized module name
    std::wstring moduleName = KeyboardManagerConstants::ModuleName;

    // Low level hook handles
    static HHOOK hookHandle;

    // Required for Unhook in old versions of Windows
    static HHOOK hookHandleCopy;

    // Static pointer to the current KeyboardManager object required for accessing the HandleKeyboardHookEvent function in the hook procedure
    // Only global or static variables can be accessed in a hook procedure CALLBACK
    static KeyboardManager* keyboardManagerObjectPtr;

    // Variable which stores all the state information to be shared between the UI and back-end
    State state;

    // Object of class which implements InputInterface. Required for calling library functions while enabling testing
    KeyboardManagerInput::Input inputHandler;

    // Auto reset event for waiting for settings changes. The event is signaled when settings are changed
    EventWaiter settingsEventWaiter;

    std::atomic_bool loadingSettings = false;

    HANDLE editorIsRunningEvent = nullptr;

    // Protects hook/priority lifecycle state (hookHandle and hookThreadHandle) when
    // StartLowlevelKeyboardHook and StopLowlevelKeyboardHook are called from
    // different threads.
    mutable std::mutex hookLifecycleMutex;

    // Real handle to the thread that installed the WH_KEYBOARD_LL hook (obtained via
    // DuplicateHandle so it remains valid even when called from another thread).
    // Used to set/restore the hook thread's scheduling priority from any thread.
    HANDLE hookThreadHandle = nullptr;

    // Thread priority that was active on the hook thread before StartLowlevelKeyboardHook
    // elevated it.  Saved so StopLowlevelKeyboardHook can restore the original value exactly.
    // Default THREAD_PRIORITY_NORMAL is a safe fallback used if GetThreadPriority() fails
    // during hook installation.
    std::atomic<int> hookThreadPriorityBeforeElevation{ THREAD_PRIORITY_NORMAL };

    // Hook procedure definition
    static LRESULT CALLBACK HookProc(int nCode, WPARAM wParam, LPARAM lParam);

    // Load settings from the file.
    void LoadSettings();

    // Function called by the hook procedure to handle the events. This is the starting point function for remapping
    intptr_t HandleKeyboardHookEvent(LowlevelKeyboardEvent* data) noexcept;
};
