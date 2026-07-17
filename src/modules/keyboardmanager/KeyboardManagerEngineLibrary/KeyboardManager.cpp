#include "pch.h"
#include "KeyboardManager.h"
#include <interface/powertoy_module_interface.h>
#include <common/SettingsAPI/settings_objects.h>
#include <common/interop/shared_constants.h>
#include <common/debug_control.h>
#include <common/utils/winapi_error.h>
#include <common/logger/logger_settings.h>

#include <keyboardmanager/common/KeyboardManagerConstants.h>
#include <keyboardmanager/common/Helpers.h>
#include <keyboardmanager/common/KeyboardEventHandlers.h>
#include <ctime>

#include "KeyboardEventHandlers.h"
#include "trace.h"

HHOOK KeyboardManager::hookHandleCopy;
HHOOK KeyboardManager::hookHandle;
KeyboardManager* KeyboardManager::keyboardManagerObjectPtr;

namespace
{
    DWORD mainThreadId = {};
}

KeyboardManager::KeyboardManager()
{
    mainThreadId = GetCurrentThreadId();

    // Load the initial settings.
    LoadSettings();

    // Set the static pointer to the newest object of the class
    keyboardManagerObjectPtr = this;

    std::filesystem::path modulePath(PTSettingsHelper::get_module_save_folder_location(moduleName));
    auto changeSettingsCallback = [this](DWORD err) {
        Logger::trace(L"{} event was signaled", KeyboardManagerConstants::SettingsEventName);
        if (err != ERROR_SUCCESS)
        {
            Logger::error(L"Failed to watch settings changes. {}", get_last_error_or_default(err));
        }

        loadingSettings = true;
        bool loadedSuccessfully = false;
        try
        {
            LoadSettings();
            loadedSuccessfully = true;
        }
        catch (...)
        {
            Logger::error("Failed to load settings");
        }

        loadingSettings = false;

        if (!loadedSuccessfully)
            return;

        const bool newHasRemappings = HasRegisteredRemappingsUnchecked();
        bool hasActiveHook = false;
        {
            std::lock_guard<std::mutex> lock(hookLifecycleMutex);
            hasActiveHook = hookHandle != nullptr;
        }

        // We didn't have any bindings before and we have now
        if (newHasRemappings && !hasActiveHook)
            PostThreadMessageW(mainThreadId, StartHookMessageID, 0, 0);

        // All bindings were removed
        if (!newHasRemappings && hasActiveHook)
            StopLowlevelKeyboardHook();
    };

    editorIsRunningEvent = CreateEvent(nullptr, true, false, KeyboardManagerConstants::EditorWindowEventName.c_str());
    settingsEventWaiter.start(KeyboardManagerConstants::SettingsEventName, changeSettingsCallback);
}

void KeyboardManager::LoadSettings()
{
    bool loadedSuccessful = state.LoadSettings();
    if (!loadedSuccessful)
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(500));

        // retry once
        state.LoadSettings();
    }
    try
    {
        // Send telemetry about configured key/shortcut to key/shortcut mappings, OS an app specific level.
        Trace::SendKeyAndShortcutRemapLoadedConfiguration(state);
    }
    catch (...)
    {
        try
        {
            Logger::error("Failed to send telemetry for the configured remappings.");
            // Try not to crash the app sending telemetry. Everything inside a try.
            Trace::ErrorSendingKeyAndShortcutRemapLoadedConfiguration();
        }
        catch (...)
        {

        }
    }
}

LRESULT CALLBACK KeyboardManager::HookProc(int nCode, const WPARAM wParam, const LPARAM lParam)
{
    LowlevelKeyboardEvent event{};
    if (nCode == HC_ACTION)
    {
        event.lParam = reinterpret_cast<KBDLLHOOKSTRUCT*>(lParam);
        event.wParam = wParam;
        event.lParam->vkCode = Helpers::EncodeKeyNumpadOrigin(event.lParam->vkCode, event.lParam->flags & LLKHF_EXTENDED);

        if (keyboardManagerObjectPtr->HandleKeyboardHookEvent(&event) == 1)
        {
            // Reset Num Lock whenever a NumLock key down event is suppressed since Num Lock key state change occurs before it is intercepted by low level hooks
            if (event.lParam->vkCode == VK_NUMLOCK && (event.wParam == WM_KEYDOWN || event.wParam == WM_SYSKEYDOWN) && event.lParam->dwExtraInfo != KeyboardManagerConstants::KEYBOARDMANAGER_SUPPRESS_FLAG)
            {
                KeyboardEventHandlers::SetNumLockToPreviousState(keyboardManagerObjectPtr->inputHandler);
            }
            return 1;
        }
    }

    return CallNextHookEx(hookHandleCopy, nCode, wParam, lParam);
}

void KeyboardManager::StartLowlevelKeyboardHook()
{
#if defined(DISABLE_LOWLEVEL_HOOKS_WHEN_DEBUGGED)
    if (IsDebuggerPresent())
    {
        return;
    }
#endif

    std::lock_guard<std::mutex> lock(hookLifecycleMutex);
    if (hookHandle)
    {
        return;
    }

    // Raise the hook thread's priority so that WH_KEYBOARD_LL callbacks are
    // dispatched promptly even under high CPU load.  Windows imposes a
    // LowLevelHooksTimeout (default ~300 ms) and silently bypasses the hook
    // when the owning thread doesn't respond in time; TIME_CRITICAL scheduling
    // ensures the thread is ready before that deadline expires.
    // NOTE: HookProc must remain strictly non-blocking so it always returns
    // well within the timeout.
    //
    // StopLowlevelKeyboardHook can be called from a different thread (settings
    // watcher), so we duplicate the current thread handle to obtain a real
    // handle that stays valid across threads.
    HANDLE realHandle = nullptr;
    if (!DuplicateHandle(GetCurrentProcess(), GetCurrentThread(),
                         GetCurrentProcess(), &realHandle,
                         THREAD_SET_INFORMATION | THREAD_QUERY_INFORMATION, FALSE, 0))
    {
        DWORD errorCode = GetLastError();
        Logger::warn(L"DuplicateHandle failed ({}); thread priority will not be elevated for the keyboard hook.", errorCode);
    }

    if (realHandle)
    {
        int savedPriority = GetThreadPriority(realHandle);
        if (savedPriority == THREAD_PRIORITY_ERROR_RETURN)
        {
            DWORD errorCode = GetLastError();
            Logger::warn(L"GetThreadPriority() failed ({}); using THREAD_PRIORITY_NORMAL as the restore value.", errorCode);
            savedPriority = THREAD_PRIORITY_NORMAL;
        }
        if (!SetThreadPriority(realHandle, THREAD_PRIORITY_TIME_CRITICAL))
        {
            DWORD errorCode = GetLastError();
            Logger::warn(L"SetThreadPriority(TIME_CRITICAL) failed ({}); hook may miss events under high CPU load.", errorCode);
        }
        hookThreadPriorityBeforeElevation.store(savedPriority, std::memory_order_release);
    }

    hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, HookProc, GetModuleHandle(NULL), NULL);
    hookHandleCopy = hookHandle;
    if (!hookHandle)
    {
        // Capture the error immediately before any other Win32 call can overwrite it.
        DWORD errorCode = GetLastError();

        // Hook installation failed.  Restore thread priority so a subsequent
        // StartLowlevelKeyboardHook call starts from the real original value.
        if (realHandle)
        {
            if (!SetThreadPriority(realHandle, hookThreadPriorityBeforeElevation.load(std::memory_order_acquire)))
            {
                DWORD restoreErrorCode = GetLastError();
                Logger::warn(L"SetThreadPriority() failed while restoring thread priority after hook installation failure (error {}).", restoreErrorCode);
            }
            CloseHandle(realHandle);
        }

        show_last_error_message(L"SetWindowsHookEx", errorCode, L"PowerToys - Keyboard Manager");
        auto errorMessage = get_last_error_message(errorCode);
        Trace::Error(errorCode, errorMessage.has_value() ? errorMessage.value() : L"", L"StartLowlevelKeyboardHook::SetWindowsHookEx");
    }
    else
    {
        // Hook is active; take ownership of the duplicated handle so
        // StopLowlevelKeyboardHook can restore the priority from any thread.
        hookThreadHandle = realHandle;
    }
}

void KeyboardManager::StopLowlevelKeyboardHook()
{
    std::lock_guard<std::mutex> lock(hookLifecycleMutex);
    if (!hookHandle)
    {
        return;
    }

    if (!UnhookWindowsHookEx(hookHandle))
    {
        DWORD errorCode = GetLastError();
        Logger::warn(L"UnhookWindowsHookEx() failed ({}); keeping hook lifecycle state so stop can be retried.", errorCode);
        return;
    }

    hookHandle = nullptr;

    // Restore the thread priority that was active before the hook was installed.
    // Use the stored real handle so this works even when called from another thread.
    if (hookThreadHandle)
    {
        if (!SetThreadPriority(hookThreadHandle, hookThreadPriorityBeforeElevation.load(std::memory_order_acquire)))
        {
            DWORD errorCode = GetLastError();
            Logger::warn(L"SetThreadPriority() failed while restoring thread priority after unhooking (error {}).", errorCode);
        }
        CloseHandle(hookThreadHandle);
        hookThreadHandle = nullptr;
    }
}

bool KeyboardManager::HasRegisteredRemappings() const
{
    constexpr int MaxAttempts = 5;

    if (loadingSettings)
    {
        for (int currentAttempt = 0; currentAttempt < MaxAttempts; ++currentAttempt)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(500));
            if (!loadingSettings)
                break;
        }
    }

    // Assume that we have registered remappings to be on the safe side if we couldn't check
    if (loadingSettings)
        return true;

    return HasRegisteredRemappingsUnchecked();
}

bool KeyboardManager::HasRegisteredRemappingsUnchecked() const
{
    return !(state.appSpecificShortcutReMap.empty() && state.appSpecificShortcutReMapSortedKeys.empty() && state.osLevelShortcutReMap.empty() && state.osLevelShortcutReMapSortedKeys.empty() && state.singleKeyReMap.empty() && state.singleKeyToTextReMap.empty());
}

intptr_t KeyboardManager::HandleKeyboardHookEvent(LowlevelKeyboardEvent* data) noexcept
{
    if (loadingSettings)
    {
        return 0;
    }

    // Suspend remapping if remap key/shortcut window is opened
    if (editorIsRunningEvent != nullptr && WaitForSingleObject(editorIsRunningEvent, 0) == WAIT_OBJECT_0)
    {
        return 0;
    }

    // If key has suppress flag, then suppress it
    if (data->lParam->dwExtraInfo == KeyboardManagerConstants::KEYBOARDMANAGER_SUPPRESS_FLAG)
    {
        return 1;
    }

    // Remap a key
    intptr_t SingleKeyRemapResult = KeyboardEventHandlers::HandleSingleKeyRemapEvent(inputHandler, data, state);

    // Single key remaps have priority. If a key is remapped, only the remapped version should be visible to the shortcuts and hence the event should be suppressed here.
    if (SingleKeyRemapResult == 1)
    {
        return 1;
    }

    /* This feature has not been enabled (code from proof of concept stage)
        // Remap a key to behave like a modifier instead of a toggle
        intptr_t SingleKeyToggleToModResult = KeyboardEventHandlers::HandleSingleKeyToggleToModEvent(inputHandler, data, keyboardManagerState);
    */

    // Handle an app-specific shortcut remapping
    intptr_t AppSpecificShortcutRemapResult = KeyboardEventHandlers::HandleAppSpecificShortcutRemapEvent(inputHandler, data, state);

    // If an app-specific shortcut is remapped then the os-level shortcut remapping should be suppressed.
    if (AppSpecificShortcutRemapResult == 1)
    {
        return 1;
    }

    intptr_t SingleKeyToTextRemapResult = KeyboardEventHandlers::HandleSingleKeyToTextRemapEvent(inputHandler, data, state);

    if (SingleKeyToTextRemapResult == 1)
    {
        return 1;
    }

    // Handle an os-level shortcut remapping
    return KeyboardEventHandlers::HandleOSLevelShortcutRemapEvent(inputHandler, data, state);
}
