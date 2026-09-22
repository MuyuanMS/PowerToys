// dllmain.cpp : Defines the entry point for the DLL application.
#include "pch.h"

#include <cmath>
#include <interface/powertoy_module_interface.h>
#include "trace.h"
#include "Generated Files/resource.h"
#include <common/logger/logger.h>
#include <common/SettingsAPI/settings_objects.h>
#include <common/utils/resources.h>

#include "ScreenTranslatorConstants.h"
#include <common/interop/shared_constants.h>
#include <common/utils/logger_helper.h>
#include <common/utils/winapi_error.h>
#include <common/utils/package.h>

BOOL APIENTRY DllMain(HMODULE /*hModule*/,
                      DWORD ul_reason_for_call,
                      LPVOID /*lpReserved*/)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Trace::RegisterProvider();
        break;
    case DLL_THREAD_ATTACH:
        break;
    case DLL_THREAD_DETACH:
        break;
    case DLL_PROCESS_DETACH:
        Trace::UnregisterProvider();
        break;
    }

    return TRUE;
}

namespace
{
    const wchar_t JSON_KEY_PROPERTIES[] = L"properties";
    const wchar_t JSON_KEY_WIN[] = L"win";
    const wchar_t JSON_KEY_ALT[] = L"alt";
    const wchar_t JSON_KEY_CTRL[] = L"ctrl";
    const wchar_t JSON_KEY_SHIFT[] = L"shift";
    const wchar_t JSON_KEY_CODE[] = L"code";
    const wchar_t JSON_KEY_ACTIVATION_SHORTCUT[] = L"ActivationShortcut";
    const wchar_t JSON_KEY_CURRENT_SCREEN_SHORTCUT[] = L"CurrentScreenShortcut";
    const wchar_t JSON_KEY_ACTIVE_WINDOW_SHORTCUT[] = L"ActiveWindowShortcut";
    const wchar_t JSON_KEY_SCAN_TEXT_SHORTCUT[] = L"ScanTextShortcut";
    const wchar_t CURRENT_SCREEN_EVENT[] = L"Local\\PowerToys_ScreenTranslator_CurrentScreenEvent-4ec42bb8-08cf-4d8c-a799-910c49020b75";
    const wchar_t ACTIVE_WINDOW_EVENT[] = L"Local\\PowerToys_ScreenTranslator_ActiveWindowEvent-a681d2ea-e2d8-430e-9bac-a1339fefd4ac";
    const wchar_t SCAN_TEXT_EVENT[] = L"Local\\PowerToys_ScreenTranslator_ScanTextEvent-61d415d8-fc8e-4704-b18e-a76e3d7b28c1";
    const wchar_t ACTIVE_WINDOW_SNAPSHOT[] = L"Local\\PowerToys_ScreenTranslator_ActiveWindowSnapshot-7bb7644a-ecaa-4baa-8b79-3df430451f20";

#pragma pack(push, 1)
    struct ActiveWindowSnapshot
    {
        unsigned long long windowHandle;
        LONG left;
        LONG top;
        LONG right;
        LONG bottom;
        LONG isValid;
    };
#pragma pack(pop)

    static_assert(sizeof(ActiveWindowSnapshot) == 28);
}

class ScreenTranslatorModule : public PowertoyModuleIface
{
private:
    bool m_enabled = false;

    std::wstring app_name;
    std::wstring app_key;

    HANDLE m_hProcess = nullptr;

    Hotkey m_hotkey;
    Hotkey m_currentScreenHotkey;
    Hotkey m_activeWindowHotkey;
    Hotkey m_scanTextHotkey;

    HANDLE m_hInvokeEvent = nullptr;
    HANDLE m_hCurrentScreenEvent = nullptr;
    HANDLE m_hActiveWindowEvent = nullptr;
    HANDLE m_hScanTextEvent = nullptr;
    HANDLE m_hTerminateEvent = nullptr;
    HANDLE m_hActiveWindowSnapshot = nullptr;

    void capture_active_window_snapshot()
    {
        if (!m_hActiveWindowSnapshot)
        {
            return;
        }

        ActiveWindowSnapshot snapshot{};
        HWND foregroundWindow = GetForegroundWindow();
        RECT windowRect{};
        if (foregroundWindow &&
            IsWindowVisible(foregroundWindow) &&
            GetWindowRect(foregroundWindow, &windowRect) &&
            windowRect.right > windowRect.left &&
            windowRect.bottom > windowRect.top)
        {
            snapshot.windowHandle = reinterpret_cast<unsigned long long>(foregroundWindow);
            snapshot.left = windowRect.left;
            snapshot.top = windowRect.top;
            snapshot.right = windowRect.right;
            snapshot.bottom = windowRect.bottom;
            snapshot.isValid = TRUE;
        }

        void* view = MapViewOfFile(m_hActiveWindowSnapshot, FILE_MAP_WRITE, 0, 0, sizeof(snapshot));
        if (view)
        {
            CopyMemory(view, &snapshot, sizeof(snapshot));
            UnmapViewOfFile(view);
        }
        else
        {
            Logger::warn(L"ScreenTranslator could not write the active-window snapshot. Error: {}", get_last_error_or_default(GetLastError()));
        }
    }

    void parse_hotkey(PowerToysSettings::PowerToyValues& settings)
    {
        m_scanTextHotkey.win = true;
        m_scanTextHotkey.ctrl = false;
        m_scanTextHotkey.alt = true;
        m_scanTextHotkey.shift = true;
        m_scanTextHotkey.key = 'T';

        auto settingsObject = settings.get_raw_json();
        if (settingsObject.GetView().Size())
        {
            try
            {
                auto properties = settingsObject.GetNamedObject(JSON_KEY_PROPERTIES);
                auto read_hotkey = [&](const wchar_t* key, Hotkey& hotkey) {
                    try
                    {
                        auto jsonHotkeyObject = properties.GetNamedObject(key);
                        Hotkey parsedHotkey = hotkey;
                        parsedHotkey.win = jsonHotkeyObject.GetNamedBoolean(JSON_KEY_WIN);
                        parsedHotkey.alt = jsonHotkeyObject.GetNamedBoolean(JSON_KEY_ALT);
                        parsedHotkey.shift = jsonHotkeyObject.GetNamedBoolean(JSON_KEY_SHIFT);
                        parsedHotkey.ctrl = jsonHotkeyObject.GetNamedBoolean(JSON_KEY_CTRL);

                        const double keyCode = jsonHotkeyObject.GetNamedNumber(JSON_KEY_CODE);
                        if (keyCode < 1 || keyCode > 0xFF || std::trunc(keyCode) != keyCode)
                        {
                            throw std::invalid_argument("Invalid shortcut key code");
                        }

                        parsedHotkey.key = static_cast<unsigned char>(keyCode);
                        hotkey = parsedHotkey;
                    }
                    catch (...)
                    {
                        Logger::info(L"ScreenTranslator shortcut setting '{}' is missing or invalid; using its default", key);
                    }
                };
                read_hotkey(JSON_KEY_ACTIVATION_SHORTCUT, m_hotkey);
                read_hotkey(JSON_KEY_CURRENT_SCREEN_SHORTCUT, m_currentScreenHotkey);
                read_hotkey(JSON_KEY_ACTIVE_WINDOW_SHORTCUT, m_activeWindowHotkey);
                read_hotkey(JSON_KEY_SCAN_TEXT_SHORTCUT, m_scanTextHotkey);
                if (m_scanTextHotkey.win &&
                    m_scanTextHotkey.ctrl &&
                    m_scanTextHotkey.shift &&
                    !m_scanTextHotkey.alt &&
                    m_scanTextHotkey.key == 'B')
                {
                    Logger::info("ScreenTranslator replacing the reserved Windows graphics-reset shortcut with Win+Alt+Shift+T");
                    m_scanTextHotkey.win = true;
                    m_scanTextHotkey.ctrl = false;
                    m_scanTextHotkey.alt = true;
                    m_scanTextHotkey.shift = true;
                    m_scanTextHotkey.key = 'T';
                }
            }
            catch (...)
            {
                Logger::error("Failed to initialize ScreenTranslator activation shortcut");
            }
        }
        else
        {
            Logger::info("ScreenTranslator settings are empty");
        }

        if (!m_hotkey.key)
        {
            Logger::info("ScreenTranslator using default shortcut Win+Ctrl+T");
            m_hotkey.win = true;
            m_hotkey.alt = false;
            m_hotkey.shift = false;
            m_hotkey.ctrl = true;
            m_hotkey.key = 'T';
        }

        if (!m_currentScreenHotkey.key)
        {
            m_currentScreenHotkey.win = true;
            m_currentScreenHotkey.ctrl = true;
            m_currentScreenHotkey.alt = false;
            m_currentScreenHotkey.shift = true;
            m_currentScreenHotkey.key = 'T';
        }

        if (!m_activeWindowHotkey.key)
        {
            m_activeWindowHotkey.win = true;
            m_activeWindowHotkey.ctrl = true;
            m_activeWindowHotkey.alt = true;
            m_activeWindowHotkey.shift = false;
            m_activeWindowHotkey.key = 'T';
        }
    }

    bool is_process_running()
    {
        return m_hProcess && (WaitForSingleObject(m_hProcess, 0) == WAIT_TIMEOUT);
    }

    void launch_process()
    {
        Logger::trace(L"Starting ScreenTranslator process");
        unsigned long powertoys_pid = GetCurrentProcessId();

        std::wstring executable_args = std::to_wstring(powertoys_pid);

        SHELLEXECUTEINFOW sei{ sizeof(sei) };
        sei.fMask = { SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI };
        sei.lpFile = L"WinUI3Apps\\PowerToys.ScreenTranslator.exe";
        sei.nShow = SW_SHOWNORMAL;
        sei.lpParameters = executable_args.data();
        if (!ShellExecuteExW(&sei))
        {
            sei.lpFile = L"PowerToys.ScreenTranslator.exe";
            if (!ShellExecuteExW(&sei))
            {
                Logger::error(L"ScreenTranslator failed to start. {}", get_last_error_or_default(GetLastError()));
                m_hProcess = nullptr;
                return;
            }
        }

        Logger::trace("Successfully started the ScreenTranslator process");
        if (m_hProcess)
        {
            CloseHandle(m_hProcess);
        }
        m_hProcess = sei.hProcess;
    }

    void init_settings()
    {
        try
        {
            PowerToysSettings::PowerToyValues settings =
                PowerToysSettings::PowerToyValues::load_from_settings_file(get_key());

            parse_hotkey(settings);
        }
        catch (std::exception&)
        {
            Logger::warn(L"An exception occurred while loading ScreenTranslator settings file");
        }
    }

public:
    ScreenTranslatorModule()
    {
        app_name = GET_RESOURCE_STRING_FALLBACK(IDS_SCREENTRANSLATOR_NAME, L"Screen Translator");
        app_key = ScreenTranslatorConstants::ModuleKey;
        LoggerHelpers::init_logger(app_key, L"ModuleInterface", "ScreenTranslator");
        m_hInvokeEvent = CreateDefaultEvent(CommonSharedConstants::SHOW_SCREEN_TRANSLATOR_SHARED_EVENT);
        m_hCurrentScreenEvent = CreateEventW(nullptr, FALSE, FALSE, CURRENT_SCREEN_EVENT);
        m_hActiveWindowEvent = CreateEventW(nullptr, FALSE, FALSE, ACTIVE_WINDOW_EVENT);
        m_hScanTextEvent = CreateEventW(nullptr, FALSE, FALSE, SCAN_TEXT_EVENT);
        m_hActiveWindowSnapshot = CreateFileMappingW(
            INVALID_HANDLE_VALUE,
            nullptr,
            PAGE_READWRITE,
            0,
            sizeof(ActiveWindowSnapshot),
            ACTIVE_WINDOW_SNAPSHOT);
        if (!m_hActiveWindowSnapshot)
        {
            Logger::error(L"ScreenTranslator could not create the active-window snapshot mapping. Error: {}", get_last_error_or_default(GetLastError()));
        }
        m_hTerminateEvent = CreateDefaultEvent(CommonSharedConstants::TERMINATE_SCREEN_TRANSLATOR_SHARED_EVENT);
        init_settings();
    }

    virtual void destroy() override
    {
        Logger::trace("ScreenTranslator::destroy()");
        if (m_enabled)
        {
            disable();
        }
        if (m_hInvokeEvent)
        {
            CloseHandle(m_hInvokeEvent);
            m_hInvokeEvent = nullptr;
        }
        if (m_hTerminateEvent)
        {
            CloseHandle(m_hTerminateEvent);
            m_hTerminateEvent = nullptr;
        }
        if (m_hCurrentScreenEvent)
        {
            CloseHandle(m_hCurrentScreenEvent);
            m_hCurrentScreenEvent = nullptr;
        }
        if (m_hActiveWindowEvent)
        {
            CloseHandle(m_hActiveWindowEvent);
            m_hActiveWindowEvent = nullptr;
        }
        if (m_hScanTextEvent)
        {
            CloseHandle(m_hScanTextEvent);
            m_hScanTextEvent = nullptr;
        }
        if (m_hActiveWindowSnapshot)
        {
            CloseHandle(m_hActiveWindowSnapshot);
            m_hActiveWindowSnapshot = nullptr;
        }
        if (m_hProcess)
        {
            CloseHandle(m_hProcess);
            m_hProcess = nullptr;
        }
        delete this;
    }

    virtual const wchar_t* get_name() override
    {
        return app_name.c_str();
    }

    virtual const wchar_t* get_key() override
    {
        return app_key.c_str();
    }

    virtual powertoys_gpo::gpo_rule_configured_t gpo_policy_enabled_configuration() override
    {
        return powertoys_gpo::gpo_rule_configured_t::gpo_rule_configured_not_configured;
    }

    virtual bool get_config(wchar_t* buffer, int* buffer_size) override
    {
        HINSTANCE hinstance = reinterpret_cast<HINSTANCE>(&__ImageBase);

        PowerToysSettings::Settings settings(hinstance, get_name());
        settings.set_description(GET_RESOURCE_STRING_FALLBACK(IDS_SCREENTRANSLATOR_SETTINGS_DESC, L"Translate on-screen text in-place."));
        return settings.serialize_to_buffer(buffer, buffer_size);
    }

    virtual void call_custom_action(const wchar_t* /*action*/) override
    {
    }

    virtual void set_config(const wchar_t* config) override
    {
        try
        {
            PowerToysSettings::PowerToyValues values =
                PowerToysSettings::PowerToyValues::from_json_string(config, get_key());

            parse_hotkey(values);
            values.save_to_settings_file();
        }
        catch (std::exception& ex)
        {
            Logger::error("Failed to save ScreenTranslator settings: {}", ex.what());
        }
    }

    virtual void send_settings_telemetry() override
    {
        Logger::info("ScreenTranslator send settings telemetry");
    }

    virtual bool is_enabled() override
    {
        return m_enabled;
    }

    virtual void enable() override
    {
        Logger::trace("ScreenTranslator::enable()");
        if (m_hInvokeEvent)
        {
            ResetEvent(m_hInvokeEvent);
        }
        if (m_hCurrentScreenEvent)
        {
            ResetEvent(m_hCurrentScreenEvent);
        }
        if (m_hActiveWindowEvent)
        {
            ResetEvent(m_hActiveWindowEvent);
        }
        if (m_hScanTextEvent)
        {
            ResetEvent(m_hScanTextEvent);
        }
        launch_process();
        m_enabled = true;
        Trace::EnableScreenTranslator(true);
    }

    virtual void disable() override
    {
        Logger::trace("ScreenTranslator::disable()");
        if (m_enabled)
        {
            if (m_hInvokeEvent)
            {
                ResetEvent(m_hInvokeEvent);
            }
            if (m_hTerminateEvent)
            {
                SetEvent(m_hTerminateEvent);
            }
            if (m_hProcess)
            {
                WaitForSingleObject(m_hProcess, 1500);
                if (is_process_running())
                {
                    TerminateProcess(m_hProcess, 1);
                }

                CloseHandle(m_hProcess);
                m_hProcess = nullptr;
            }
        }

        m_enabled = false;
        Trace::EnableScreenTranslator(false);
    }

    virtual bool on_hotkey(size_t hotkeyId) override
    {
        if (m_enabled)
        {
            Logger::trace(L"ScreenTranslator hotkey pressed");
            if (hotkeyId == 2)
            {
                capture_active_window_snapshot();
            }

            if (!is_process_running())
            {
                launch_process();
            }

            HANDLE eventToSignal = hotkeyId == 1 ? m_hCurrentScreenEvent :
                                   hotkeyId == 2 ? m_hActiveWindowEvent :
                                   hotkeyId == 3 ? m_hScanTextEvent :
                                                   m_hInvokeEvent;
            if (eventToSignal)
            {
                SetEvent(eventToSignal);
            }
            return true;
        }

        return false;
    }

    virtual size_t get_hotkeys(Hotkey* hotkeys, size_t buffer_size) override
    {
        constexpr size_t hotkeyCount = 4;
        if (hotkeys && buffer_size >= hotkeyCount)
        {
            hotkeys[0] = m_hotkey;
            hotkeys[1] = m_currentScreenHotkey;
            hotkeys[2] = m_activeWindowHotkey;
            hotkeys[3] = m_scanTextHotkey;
        }

        return hotkeyCount;
    }
};

extern "C" __declspec(dllexport) PowertoyModuleIface* __cdecl powertoy_create()
{
    return new ScreenTranslatorModule();
}
