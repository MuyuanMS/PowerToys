#pragma once
#include <windows.h>
#include <comdef.h>
#include <wbemidl.h>
#include <functional>
#include <thread>
#include <atomic>
#include <optional>
#include <future>
#include <memory>
#include <algorithm>
#include <chrono>

#pragma comment(lib, "wbemuuid.lib")
#ifdef _DEBUG
#pragma comment(lib, "comsuppwd.lib") // _bstr_t / _com_util COM support
#else
#pragma comment(lib, "comsuppw.lib") // _bstr_t / _com_util COM support
#endif

// Polls the WMI WmiMonitorBrightness class every few seconds and fires a callback
// when the brightness value changes. Works for laptop/tablet integrated displays
// whose brightness is driven by an ambient light sensor (ALS) or by the user.
class BrightnessObserver
{
public:
    // callback receives the new brightness level (0-100)
    explicit BrightnessObserver(std::function<void(int)> callback, int pollIntervalSeconds = 5)
        : _state(std::make_shared<State>(std::move(callback), pollIntervalSeconds))
    {
        _thread = std::thread([state = _state]() { Run(state); });
    }

    ~BrightnessObserver()
    {
        Stop();
    }

    void Stop()
    {
        _state->stop = true;
        if (_thread.joinable())
        {
            auto done = _state->done.get_future();
            if (done.wait_for(std::chrono::seconds(2)) == std::future_status::ready)
            {
                _thread.join();
            }
            else
            {
                _thread.detach();
            }
        }
    }

private:
    struct State
    {
        State(std::function<void(int)> callback, int pollIntervalSeconds) :
            callback(std::move(callback)),
            pollInterval(pollIntervalSeconds)
        {
        }

        std::function<void(int)> callback;
        int pollInterval;
        std::atomic<bool> stop = false;
        std::promise<void> done;
    };

    std::shared_ptr<State> _state;
    std::thread _thread;

    static void WaitForNextPoll(const std::shared_ptr<State>& state)
    {
        for (int i = 0; i < state->pollInterval && !state->stop; ++i)
        {
            std::this_thread::sleep_for(std::chrono::seconds(1));
        }
    }

    static void Run(const std::shared_ptr<State>& state)
    {
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        bool coInitializeCalledHere = SUCCEEDED(hr);

        int lastBrightness = -1;

        // Bounded wait so a stalled WMI provider cannot block Next() indefinitely;
        // this keeps Stop()/join() responsive to _stop between polls.
        constexpr long kNextTimeoutMs = 1000;

        while (!state->stop)
        {
            IWbemLocator* pLoc = nullptr;
            IWbemServices* pSvc = nullptr;

            while (!state->stop && !pSvc)
            {
                hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
                                      IID_IWbemLocator, reinterpret_cast<LPVOID*>(&pLoc));
                if (FAILED(hr))
                {
                    WaitForNextPoll(state);
                    continue;
                }

                hr = pLoc->ConnectServer(_bstr_t(L"ROOT\\WMI"), nullptr, nullptr, nullptr,
                                         WBEM_FLAG_CONNECT_USE_MAX_WAIT, nullptr, nullptr, &pSvc);
                if (FAILED(hr))
                {
                    pLoc->Release();
                    pLoc = nullptr;
                    WaitForNextPoll(state);
                    continue;
                }

                hr = CoSetProxyBlanket(pSvc, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, nullptr,
                                       RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE,
                                       nullptr, EOAC_NONE);
                if (FAILED(hr))
                {
                    pSvc->Release();
                    pSvc = nullptr;
                    pLoc->Release();
                    pLoc = nullptr;
                    WaitForNextPoll(state);
                }
            }

            while (!state->stop && pSvc)
            {
                IEnumWbemClassObject* pEnum = nullptr;
                hr = pSvc->ExecQuery(
                    _bstr_t(L"WQL"),
                    _bstr_t(L"SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE"),
                    WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                    nullptr, &pEnum);

                if (FAILED(hr))
                {
                    break;
                }

                if (pEnum)
                {
                    IWbemClassObject* pObj = nullptr;
                    ULONG returned = 0;
                    std::optional<int> maxBrightness;
                    while (!state->stop &&
                           pEnum->Next(kNextTimeoutMs, 1, &pObj, &returned) == WBEM_S_NO_ERROR &&
                           returned)
                    {
                        VARIANT vt;
                        VariantInit(&vt);
                        if (SUCCEEDED(pObj->Get(L"CurrentBrightness", 0, &vt, nullptr, nullptr)))
                        {
                            int brightness = static_cast<int>(vt.bVal);
                            maxBrightness = maxBrightness && *maxBrightness > brightness ? *maxBrightness : brightness;
                        }
                        VariantClear(&vt);
                        pObj->Release();
                        pObj = nullptr;
                    }

                    if (!state->stop && maxBrightness && *maxBrightness != lastBrightness)
                    {
                        lastBrightness = *maxBrightness;
                        try
                        {
                            state->callback(lastBrightness);
                        }
                        catch (...) {}
                    }
                    else if (!state->stop && !maxBrightness && lastBrightness != -1)
                    {
                        lastBrightness = -1;
                        try
                        {
                            state->callback(lastBrightness);
                        }
                        catch (...) {}
                    }

                    pEnum->Release();
                }

                WaitForNextPoll(state);
            }

            if (pSvc) pSvc->Release();
            if (pLoc) pLoc->Release();
            if (!state->stop) WaitForNextPoll(state);
        }

        if (coInitializeCalledHere) CoUninitialize();
        state->done.set_value();
    }
};
