#pragma once
#include <windows.h>
#include <comdef.h>
#include <wbemidl.h>
#include <functional>
#include <thread>
#include <atomic>
#include <optional>

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
        : _callback(std::move(callback)), _pollInterval(pollIntervalSeconds), _stop(false)
    {
        _thread = std::thread([this]() { Run(); });
    }

    ~BrightnessObserver()
    {
        Stop();
    }

    void Stop()
    {
        _stop = true;
        if (_thread.joinable())
            _thread.join();
    }

private:
    std::function<void(int)> _callback;
    int _pollInterval;
    std::atomic<bool> _stop;
    std::thread _thread;

    void WaitForNextPoll()
    {
        for (int i = 0; i < _pollInterval && !_stop; ++i)
        {
            std::this_thread::sleep_for(std::chrono::seconds(1));
        }
    }

    void Run()
    {
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        bool coInitializeCalledHere = SUCCEEDED(hr);

        int lastBrightness = -1;

        // Bounded wait so a stalled WMI provider cannot block Next() indefinitely;
        // this keeps Stop()/join() responsive to _stop between polls.
        constexpr long kNextTimeoutMs = 1000;

        while (!_stop)
        {
            IWbemLocator* pLoc = nullptr;
            IWbemServices* pSvc = nullptr;

            while (!_stop && !pSvc)
            {
                hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
                                      IID_IWbemLocator, reinterpret_cast<LPVOID*>(&pLoc));
                if (FAILED(hr))
                {
                    WaitForNextPoll();
                    continue;
                }

                hr = pLoc->ConnectServer(_bstr_t(L"ROOT\\WMI"), nullptr, nullptr, nullptr,
                                         WBEM_FLAG_CONNECT_USE_MAX_WAIT, nullptr, nullptr, &pSvc);
                if (FAILED(hr))
                {
                    pLoc->Release();
                    pLoc = nullptr;
                    WaitForNextPoll();
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
                    WaitForNextPoll();
                }
            }

            while (!_stop && pSvc)
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
                    if (pEnum->Next(kNextTimeoutMs, 1, &pObj, &returned) == WBEM_S_NO_ERROR && returned)
                    {
                        VARIANT vt;
                        VariantInit(&vt);
                        if (SUCCEEDED(pObj->Get(L"CurrentBrightness", 0, &vt, nullptr, nullptr)))
                        {
                            int brightness = static_cast<int>(vt.bVal);
                            if (brightness != lastBrightness)
                            {
                                lastBrightness = brightness;
                                try
                                {
                                    _callback(lastBrightness);
                                }
                                catch (...) {}
                            }
                        }
                        VariantClear(&vt);
                        pObj->Release();
                    }
                    pEnum->Release();
                }

                WaitForNextPoll();
            }

            if (pSvc) pSvc->Release();
            if (pLoc) pLoc->Release();
            if (!_stop) WaitForNextPoll();
        }

        if (coInitializeCalledHere) CoUninitialize();
    }
};
