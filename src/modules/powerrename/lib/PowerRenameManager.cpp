#include "pch.h"
#include "PowerRenameManager.h"
#include "PowerRenameRegEx.h" // Default RegEx handler
#include <algorithm>
#include <shlobj.h>
#include <cstring>
#include <new>
#include "helpers.h"
#include "trace.h"
#include <Renaming.h>

namespace fs = std::filesystem;

extern HINSTANCE g_hostHInst;

// The default FOF flags to use in the rename operations
#define FOF_DEFAULTFLAGS (FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOFX_SHOWELEVATIONPROMPT | FOF_RENAMEONCOLLISION)

namespace
{
    std::wstring GetShellItemPath(_In_ IShellItem* shellItem)
    {
        PWSTR path = nullptr;
        std::wstring result;
        if (shellItem != nullptr && SUCCEEDED(shellItem->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path != nullptr)
        {
            result = path;
        }

        CoTaskMemFree(path);
        return result;
    }

    bool StartsWithPathIgnoringCase(_In_ const std::wstring& path, _In_ const std::wstring& prefix)
    {
        return path.size() >= prefix.size() &&
               CompareStringOrdinal(path.c_str(), static_cast<int>(prefix.size()), prefix.c_str(), static_cast<int>(prefix.size()), TRUE) == CSTR_EQUAL &&
               (path.size() == prefix.size() || path[prefix.size()] == L'\\');
    }

    bool StartsWithPathMatchingCase(_In_ const std::wstring& path, _In_ const std::wstring& prefix)
    {
        return path.size() >= prefix.size() &&
               CompareStringOrdinal(path.c_str(), static_cast<int>(prefix.size()), prefix.c_str(), static_cast<int>(prefix.size()), FALSE) == CSTR_EQUAL &&
               (path.size() == prefix.size() || path[prefix.size()] == L'\\');
    }

    class RenameProgressSink :
        public IFileOperationProgressSink
    {
    public:
        IFACEMETHODIMP QueryInterface(_In_ REFIID riid, _Outptr_ void** ppv) override
        {
            if (ppv == nullptr)
            {
                return E_POINTER;
            }

            *ppv = nullptr;
            if (riid == IID_IUnknown || riid == IID_IFileOperationProgressSink)
            {
                *ppv = static_cast<IFileOperationProgressSink*>(this);
                AddRef();
                return S_OK;
            }

            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return InterlockedIncrement(&m_refCount);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            const ULONG refCount = InterlockedDecrement(&m_refCount);
            if (refCount == 0)
            {
                delete this;
            }

            return refCount;
        }

        IFACEMETHODIMP StartOperations() override
        {
            return S_OK;
        }

        IFACEMETHODIMP FinishOperations(_In_ HRESULT) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PreRenameItem(_In_ DWORD, _In_ IShellItem*, _In_ LPCWSTR) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PostRenameItem(_In_ DWORD, _In_ IShellItem* item, _In_ LPCWSTR, _In_ HRESULT renameHr, _In_ IShellItem* newlyCreated) override
        {
            RenameResult result;
            result.hr = renameHr;
            if (SUCCEEDED(renameHr))
            {
                result.newPath = GetShellItemPath(newlyCreated);
                if (result.newPath.empty())
                {
                    result.newPath = GetShellItemPath(item);
                }
            }

            m_renameResults.push_back(std::move(result));
            return S_OK;
        }

        IFACEMETHODIMP PreMoveItem(_In_ DWORD, _In_ IShellItem*, _In_ IShellItem*, _In_ LPCWSTR) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PostMoveItem(_In_ DWORD, _In_ IShellItem*, _In_ IShellItem*, _In_ LPCWSTR, _In_ HRESULT, _In_ IShellItem*) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PreCopyItem(_In_ DWORD, _In_ IShellItem*, _In_ IShellItem*, _In_ LPCWSTR) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PostCopyItem(_In_ DWORD, _In_ IShellItem*, _In_ IShellItem*, _In_ LPCWSTR, _In_ HRESULT, _In_ IShellItem*) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PreDeleteItem(_In_ DWORD, _In_ IShellItem*) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PostDeleteItem(_In_ DWORD, _In_ IShellItem*, _In_ HRESULT, _In_ IShellItem*) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PreNewItem(_In_ DWORD, _In_ IShellItem*, _In_ LPCWSTR) override
        {
            return S_OK;
        }

        IFACEMETHODIMP PostNewItem(_In_ DWORD, _In_ IShellItem*, _In_ LPCWSTR, _In_ LPCWSTR, _In_ DWORD, _In_ HRESULT, _In_ IShellItem*) override
        {
            return S_OK;
        }

        IFACEMETHODIMP UpdateProgress(_In_ UINT, _In_ UINT) override
        {
            return S_OK;
        }

        IFACEMETHODIMP ResetTimer() override
        {
            return S_OK;
        }

        IFACEMETHODIMP PauseTimer() override
        {
            return S_OK;
        }

        IFACEMETHODIMP ResumeTimer() override
        {
            return S_OK;
        }

        bool TryGetSuccessfulRename(_In_ size_t operationIndex, _Out_ std::wstring& newPath) const
        {
            if (operationIndex >= m_renameResults.size() || FAILED(m_renameResults[operationIndex].hr) || m_renameResults[operationIndex].newPath.empty())
            {
                return false;
            }

            newPath = m_renameResults[operationIndex].newPath;
            return true;
        }

    private:
        struct RenameResult
        {
            HRESULT hr = E_FAIL;
            std::wstring newPath;
        };

        long m_refCount = 1;
        std::vector<RenameResult> m_renameResults;
    };
}

IFACEMETHODIMP_(ULONG)
CPowerRenameManager::AddRef()
{
    return InterlockedIncrement(&m_refCount);
}

IFACEMETHODIMP_(ULONG)
CPowerRenameManager::Release()
{
    long refCount = InterlockedDecrement(&m_refCount);

    if (refCount == 0)
    {
        delete this;
    }
    return refCount;
}

IFACEMETHODIMP CPowerRenameManager::QueryInterface(_In_ REFIID riid, _Outptr_ void** ppv)
{
    static const QITAB qit[] = {
        QITABENT(CPowerRenameManager, IPowerRenameManager),
        QITABENT(CPowerRenameManager, IPowerRenameRegExEvents),
        { 0 }
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP CPowerRenameManager::Advise(_In_ IPowerRenameManagerEvents* renameOpEvents, _Out_ DWORD* cookie)
{
    CSRWExclusiveAutoLock lock(&m_lockEvents);
    m_cookie++;
    RENAME_MGR_EVENT srme;
    srme.cookie = m_cookie;
    srme.pEvents = renameOpEvents;
    renameOpEvents->AddRef();
    m_powerRenameManagerEvents.push_back(srme);

    *cookie = m_cookie;

    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::UnAdvise(_In_ DWORD cookie)
{
    HRESULT hr = E_FAIL;
    CSRWExclusiveAutoLock lock(&m_lockEvents);

    for (std::vector<RENAME_MGR_EVENT>::iterator it = m_powerRenameManagerEvents.begin(); it != m_powerRenameManagerEvents.end(); ++it)
    {
        if (it->cookie == cookie)
        {
            hr = S_OK;
            it->cookie = 0;
            if (it->pEvents)
            {
                it->pEvents->Release();
                it->pEvents = nullptr;
            }
            break;
        }
    }

    return hr;
}

IFACEMETHODIMP CPowerRenameManager::Start()
{
    return E_NOTIMPL;
}

IFACEMETHODIMP CPowerRenameManager::Stop()
{
    return E_NOTIMPL;
}

IFACEMETHODIMP CPowerRenameManager::Rename(_In_ HWND hwndParent, bool closeWindow)
{
    m_hwndParent = hwndParent;
    m_closeUIWindowAfterRenaming = closeWindow;
    return _PerformFileOperation();
}

IFACEMETHODIMP CPowerRenameManager::UpdateChildrenPath(_In_ int parentId, _In_ size_t oldParentPathSize)
{
    auto parentIt = m_renameItems.find(parentId);
    if (parentIt != m_renameItems.end())
    {
        UINT depth = 0;
        winrt::check_hresult(parentIt->second->GetDepth(&depth));

        PWSTR renamedPath = nullptr;
        winrt::check_hresult(parentIt->second->GetPath(&renamedPath));
        std::wstring renamedPathStr{ renamedPath };
        CoTaskMemFree(renamedPath);

        for (auto it = ++parentIt; it != m_renameItems.end(); ++it)
        {
            UINT nextDepth = 0;
            winrt::check_hresult(it->second->GetDepth(&nextDepth));

            if (nextDepth > depth)
            {
                // This is child, update path
                PWSTR path = nullptr;
                winrt::check_hresult(it->second->GetPath(&path));
                std::wstring pathStr{ path };
                CoTaskMemFree(path);

                if (!StartsWithPathMatchingCase(pathStr, renamedPathStr) && pathStr.size() >= oldParentPathSize)
                {
                    std::wstring newPath = pathStr.replace(0, oldParentPathSize, renamedPathStr);
                    it->second->PutPath(newPath.c_str());
                }
            }
            else
            {
                break;
            }
        }
    }

    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::GetCloseUIWindowAfterRenaming(_Out_ bool* closeUIWindowAfterRenaming)
{
    *closeUIWindowAfterRenaming = m_closeUIWindowAfterRenaming;
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::Reset()
{
    // Stop all threads and wait
    // Reset all rename items
    return E_NOTIMPL;
}

IFACEMETHODIMP CPowerRenameManager::Shutdown()
{
    _ClearRegEx();
    _Cleanup();
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::AddItem(_In_ IPowerRenameItem* pItem)
{
    HRESULT hr = E_FAIL;
    // Scope lock
    {
        CSRWExclusiveAutoLock lock(&m_lockItems);
        int id = 0;
        pItem->GetId(&id);
        // Verify the item isn't already added
        if (m_renameItems.find(id) == m_renameItems.end())
        {
            m_renameItems[id] = pItem;
            m_isVisible.push_back(true);
            pItem->AddRef();
            hr = S_OK;
        }
    }

    return hr;
}

IFACEMETHODIMP CPowerRenameManager::GetItemByIndex(_In_ UINT index, _COM_Outptr_ IPowerRenameItem** ppItem)
{
    *ppItem = nullptr;
    CSRWSharedAutoLock lock(&m_lockItems);
    HRESULT hr = E_FAIL;
    if (index < m_renameItems.size())
    {
        std::map<int, IPowerRenameItem*>::iterator it = m_renameItems.begin();
        std::advance(it, index);
        *ppItem = it->second;
        (*ppItem)->AddRef();
        hr = S_OK;
    }

    return hr;
}

IFACEMETHODIMP CPowerRenameManager::GetVisibleItemByIndex(_In_ UINT index, _COM_Outptr_ IPowerRenameItem** ppItem)
{
    *ppItem = nullptr;
    CSRWSharedAutoLock lock(&m_lockItems);
    UINT count = 0;
    HRESULT hr = E_FAIL;

    if (m_filter == PowerRenameFilters::None)
    {
        hr = GetItemByIndex(index, ppItem);
    }
    else if (SUCCEEDED(GetVisibleItemCount(&count)) && index < count)
    {
        const UINT realIndex = GetVisibleItemRealIndex(index);
        hr = GetItemByIndex(realIndex, ppItem);
    }

    return hr;
}

uint32_t CPowerRenameManager::GetVisibleItemRealIndex(const uint32_t index) const
{
    UINT realIndex = 0, visibleIndex = 0;
    for (size_t i = 0; i < m_isVisible.size(); i++)
    {
        if (m_isVisible[i] && visibleIndex == index)
        {
            realIndex = static_cast<UINT>(i);
            break;
        }
        if (m_isVisible[i])
        {
            visibleIndex++;
        }
    }

    return realIndex;
}

IFACEMETHODIMP CPowerRenameManager::GetItemById(_In_ int id, _COM_Outptr_ IPowerRenameItem** ppItem)
{
    *ppItem = nullptr;

    CSRWSharedAutoLock lock(&m_lockItems);
    HRESULT hr = E_FAIL;
    std::map<int, IPowerRenameItem*>::iterator it;
    it = m_renameItems.find(id);
    if (it != m_renameItems.end())
    {
        *ppItem = m_renameItems[id];
        (*ppItem)->AddRef();
        hr = S_OK;
    }

    return hr;
}

IFACEMETHODIMP CPowerRenameManager::GetItemCount(_Out_ UINT* count)
{
    CSRWSharedAutoLock lock(&m_lockItems);
    *count = static_cast<UINT>(m_renameItems.size());
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::SetVisible()
{
    CSRWSharedAutoLock lock(&m_lockItems);
    HRESULT hr = E_FAIL;
    UINT lastVisibleDepth = 0;
    size_t i = m_isVisible.size() - 1;
    PWSTR searchTerm = nullptr;
    for (auto rit = m_renameItems.rbegin(); rit != m_renameItems.rend(); ++rit, --i)
    {
        bool isVisible = false;
        if (m_filter == PowerRenameFilters::ShouldRename &&
            (FAILED(m_spRegEx->GetSearchTerm(&searchTerm)) || searchTerm && wcslen(searchTerm) == 0))
        {
            isVisible = true;
        }
        else
        {
            rit->second->IsItemVisible(m_filter, m_flags, &isVisible);
        }

        UINT itemDepth = 0;
        rit->second->GetDepth(&itemDepth);

        //Make an item visible if it has a least one visible subitem
        if (isVisible)
        {
            lastVisibleDepth = itemDepth;
        }
        else if (lastVisibleDepth == itemDepth + 1)
        {
            isVisible = true;
            lastVisibleDepth = itemDepth;
        }

        m_isVisible[i] = isVisible;
        hr = S_OK;
    }

    return hr;
}

IFACEMETHODIMP CPowerRenameManager::GetVisibleItemCount(_Out_ UINT* count)
{
    *count = 0;
    CSRWSharedAutoLock lock(&m_lockItems);

    if (m_filter != PowerRenameFilters::None)
    {
        SetVisible();

        for (size_t i = 0; i < m_isVisible.size(); i++)
        {
            if (m_isVisible[i])
            {
                (*count)++;
            }
        }
    }
    else
    {
        GetItemCount(count);
    }

    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::GetSelectedItemCount(_Out_ UINT* count)
{
    *count = 0;
    CSRWSharedAutoLock lock(&m_lockItems);

    for (auto it : m_renameItems)
    {
        IPowerRenameItem* pItem = it.second;
        bool selected = false;
        if (SUCCEEDED(pItem->GetSelected(&selected)) && selected)
        {
            (*count)++;
        }
    }

    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::GetRenameItemCount(_Out_ UINT* count)
{
    *count = 0;
    CSRWSharedAutoLock lock(&m_lockItems);

    for (auto it : m_renameItems)
    {
        IPowerRenameItem* pItem = it.second;
        bool shouldRename = false;
        if (SUCCEEDED(pItem->ShouldRenameItem(m_flags, &shouldRename)) && shouldRename)
        {
            (*count)++;
        }
    }

    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::GetFlags(_Out_ DWORD* flags)
{
    _EnsureRegEx();
    *flags = m_flags;
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::PutFlags(_In_ DWORD flags)
{
    if (flags != m_flags)
    {
        m_flags = flags;
        _EnsureRegEx();
        m_spRegEx->PutFlags(flags);
    }
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::GetFilter(_Out_ DWORD* filter)
{
    *filter = m_filter;
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::SwitchFilter(_In_ int)
{
    switch (m_filter)
    {
    case PowerRenameFilters::None:
        m_filter = PowerRenameFilters::ShouldRename;
        break;
    case PowerRenameFilters::ShouldRename:
        m_filter = PowerRenameFilters::None;
        break;
    }

    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::GetRenameRegEx(_COM_Outptr_ IPowerRenameRegEx** ppRegEx)
{
    *ppRegEx = nullptr;
    HRESULT hr = _EnsureRegEx();
    if (SUCCEEDED(hr))
    {
        *ppRegEx = m_spRegEx;
        (*ppRegEx)->AddRef();
    }
    return hr;
}

IFACEMETHODIMP CPowerRenameManager::PutRenameRegEx(_In_ IPowerRenameRegEx* pRegEx)
{
    _ClearRegEx();
    m_spRegEx = pRegEx;
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::GetRenameItemFactory(_COM_Outptr_ IPowerRenameItemFactory** ppItemFactory)
{
    *ppItemFactory = nullptr;
    HRESULT hr = E_FAIL;
    if (m_spItemFactory)
    {
        hr = S_OK;
        *ppItemFactory = m_spItemFactory;
        (*ppItemFactory)->AddRef();
    }
    return hr;
}

IFACEMETHODIMP CPowerRenameManager::PutRenameItemFactory(_In_ IPowerRenameItemFactory* pItemFactory)
{
    m_spItemFactory = pItemFactory;
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::OnSearchTermChanged(_In_ PCWSTR /*searchTerm*/)
{
    _PerformRegExRename();
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::OnReplaceTermChanged(_In_ PCWSTR /*replaceTerm*/)
{
    _PerformRegExRename();
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::OnFlagsChanged(_In_ DWORD flags)
{
    // Flags were updated in the rename regex.  Update our preview.
    m_flags = flags;
    _PerformRegExRename();
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::OnFileTimeChanged(_In_ SYSTEMTIME /*fileTime*/)
{
    _PerformRegExRename();
    return S_OK;
}

IFACEMETHODIMP CPowerRenameManager::OnMetadataChanged()
{
    _PerformRegExRename();
    return S_OK;
}

HRESULT CPowerRenameManager::s_CreateInstance(_Outptr_ IPowerRenameManager** ppsrm)
{
    *ppsrm = nullptr;
    CPowerRenameManager* psrm = new CPowerRenameManager();
    HRESULT hr = E_OUTOFMEMORY;
    if (psrm)
    {
        hr = psrm->_Init();
        if (SUCCEEDED(hr))
        {
            hr = psrm->QueryInterface(IID_PPV_ARGS(ppsrm));
        }
        psrm->Release();
    }
    return hr;
}

CPowerRenameManager::CPowerRenameManager() :
    m_refCount(1)
{
    InitializeCriticalSection(&m_critsecReentrancy);
}

CPowerRenameManager::~CPowerRenameManager()
{
    DeleteCriticalSection(&m_critsecReentrancy);
}

HRESULT CPowerRenameManager::_Init()
{
    // Guaranteed to succeed
    m_startFileOpWorkerEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);
    m_startRegExWorkerEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);
    m_cancelRegExWorkerEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);

    m_hwndMessage = CreateMsgWindow(g_hostHInst, s_msgWndProc, this);

    return S_OK;
}

// Custom messages for worker threads
enum
{
    SRM_REGEX_ITEM_UPDATED = (WM_APP + 1), // Single rename item processed by regex worker thread
    SRM_REGEX_ITEM_RENAMED_KEEP_UI, // Single rename item processed by rename worker thread in case UI remains opened
    SRM_REGEX_STARTED, // RegEx operation was started
    SRM_REGEX_CANCELED, // Regex operation was canceled
    SRM_REGEX_COMPLETE, // Regex worker thread completed
    SRM_FILEOP_COMPLETE // File Operation worker thread completed
};

struct WorkerThreadData
{
    HWND hwndManager = nullptr;
    HANDLE startEvent = nullptr;
    HANDLE cancelEvent = nullptr;
    HWND hwndParent = nullptr;
    CComPtr<IPowerRenameManager> spsrm;
};

// Msg-only worker window proc for communication from our worker threads
LRESULT CALLBACK CPowerRenameManager::s_msgWndProc(_In_ HWND hwnd, _In_ UINT uMsg, _In_ WPARAM wParam, _In_ LPARAM lParam)
{
    LRESULT lRes = 0;

    CPowerRenameManager* pThis = reinterpret_cast<CPowerRenameManager*>(GetWindowLongPtr(hwnd, 0));
    if (pThis != nullptr)
    {
        lRes = pThis->_WndProc(hwnd, uMsg, wParam, lParam);
        if (uMsg == WM_NCDESTROY)
        {
            SetWindowLongPtr(hwnd, 0, NULL);
            pThis->m_hwndMessage = nullptr;
        }
    }
    else
    {
        lRes = DefWindowProc(hwnd, uMsg, wParam, lParam);
    }

    return lRes;
}

LRESULT CPowerRenameManager::_WndProc(_In_ HWND hwnd, _In_ UINT msg, _In_ WPARAM wParam, _In_ LPARAM lParam)
{
    LRESULT lRes = 0;

    AddRef();

    switch (msg)
    {
    case SRM_REGEX_ITEM_UPDATED:
    {
        // Do nothing.
        break;
    }
    case SRM_REGEX_ITEM_RENAMED_KEEP_UI:
    {
        int id = static_cast<int>(lParam);
        CComPtr<IPowerRenameItem> spItem;
        if (SUCCEEDED(GetItemById(id, &spItem)))
        {
            _OnRename(spItem);
        }
        break;
    }
    case SRM_REGEX_STARTED:
        _OnRegExStarted(static_cast<DWORD>(wParam));
        break;

    case SRM_REGEX_CANCELED:
        _OnRegExCanceled(static_cast<DWORD>(wParam));
        break;

    case SRM_REGEX_COMPLETE:
        _OnRegExCompleted(static_cast<DWORD>(wParam));
        break;

    default:
        lRes = DefWindowProc(hwnd, msg, wParam, lParam);
        break;
    }

    Release();

    return lRes;
}

void CPowerRenameManager::_LogOperationTelemetry()
{
    UINT renameItemCount = 0;
    UINT selectedItemCount = 0;
    UINT totalItemCount = 0;
    DWORD flags = 0;

    GetItemCount(&totalItemCount);
    GetSelectedItemCount(&selectedItemCount);
    GetRenameItemCount(&renameItemCount);
    GetFlags(&flags);

    // Enumerate extensions used into a map
    std::map<std::wstring, int> extensionsMap;
    for (UINT i = 0; i < totalItemCount; i++)
    {
        CComPtr<IPowerRenameItem> spItem;
        if (SUCCEEDED(GetItemByIndex(i, &spItem)))
        {
            PWSTR originalName;
            if (SUCCEEDED(spItem->GetOriginalName(&originalName)))
            {
                std::wstring extension = fs::path(originalName).extension().wstring();
                std::map<std::wstring, int>::iterator it = extensionsMap.find(extension);
                if (it == extensionsMap.end())
                {
                    extensionsMap.insert({ extension, 1 });
                }
                else
                {
                    it->second++;
                }

                CoTaskMemFree(originalName);
            }
        }
    }

    std::wstring extensionList = L"";
    for (auto elem : extensionsMap)
    {
        extensionList.append(elem.first);
        extensionList.append(L":");
        extensionList.append(std::to_wstring(elem.second));
        extensionList.append(L",");
    }

    Trace::RenameOperation(totalItemCount, selectedItemCount, renameItemCount, flags, extensionList.c_str());
}

HRESULT CPowerRenameManager::_PerformFileOperation()
{
    // Do we have items to rename?
    UINT renameItemCount = 0;
    if (FAILED(GetRenameItemCount(&renameItemCount)))
    {
        return E_FAIL;
    }
    if (renameItemCount == 0)
    {
        return S_OK;
    }

    _LogOperationTelemetry();

    // Wait for existing regex thread to finish
    _WaitForRegExWorkerThread();

    // Create worker thread which will perform the actual rename
    HRESULT hr = _CreateFileOpWorkerThread();
    if (SUCCEEDED(hr))
    {
        _OnRenameStarted();

        // Signal the worker thread that they can start working. We needed to wait until we
        // were ready to process thread messages.
        SetEvent(m_startFileOpWorkerEvent);

        while (true)
        {
            // Check if worker thread has exited
            if (WaitForSingleObject(m_fileOpWorkerThreadHandle, 0) == WAIT_OBJECT_0)
            {
                break;
            }

            MSG msg;
            while (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE))
            {
                if (msg.message == SRM_FILEOP_COMPLETE)
                {
                    // Worker thread completed
                    break;
                }
                else
                {
                    TranslateMessage(&msg);
                    DispatchMessage(&msg);
                }
            }
        }

        _OnRenameCompleted();
    }

    return S_OK;
}

HRESULT CPowerRenameManager::_CreateFileOpWorkerThread()
{
    WorkerThreadData* pwtd = new WorkerThreadData;
    HRESULT hr = E_OUTOFMEMORY;
    if (pwtd)
    {
        pwtd->hwndManager = m_hwndMessage;
        pwtd->startEvent = m_startRegExWorkerEvent;
        pwtd->cancelEvent = nullptr;
        pwtd->spsrm = this;
        m_fileOpWorkerThreadHandle = CreateThread(nullptr, 0, s_fileOpWorkerThread, pwtd, 0, nullptr);
        hr = E_FAIL;
        if (m_fileOpWorkerThreadHandle)
        {
            hr = S_OK;
        }
        else
        {
            delete pwtd;
        }
    }

    return hr;
}

DWORD WINAPI CPowerRenameManager::s_fileOpWorkerThread(_In_ void* pv)
{
    if (SUCCEEDED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE)))
    {
        WorkerThreadData* pwtd = static_cast<WorkerThreadData*>(pv);
        if (pwtd)
        {
            bool closeUIWindowAfterRenaming = true;
            pwtd->spsrm->GetCloseUIWindowAfterRenaming(&closeUIWindowAfterRenaming);

            // Wait to be told we can begin
            if (WaitForSingleObject(pwtd->startEvent, INFINITE) == WAIT_OBJECT_0)
            {
                CComPtr<IPowerRenameRegEx> spRenameRegEx;
                if (SUCCEEDED(pwtd->spsrm->GetRenameRegEx(&spRenameRegEx)))
                {
                    // Create IFileOperation interface
                    CComPtr<IFileOperation> spFileOp;
                    if (SUCCEEDED(CoCreateInstance(CLSID_FileOperation, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&spFileOp))))
                    {
                        DWORD flags = 0;
                        spRenameRegEx->GetFlags(&flags);

                        UINT itemCount = 0;
                        pwtd->spsrm->GetItemCount(&itemCount);

                        // We add the items to the operation in depth-first order.  This allows child items to be
                        // renamed before parent items.

                        // First pass: find the maximum depth to properly size the matrix
                        UINT maxDepth = 0;
                        for (UINT u = 0; u < itemCount; u++)
                        {
                            CComPtr<IPowerRenameItem> spItem;
                            if (SUCCEEDED(pwtd->spsrm->GetItemByIndex(u, &spItem)))
                            {
                                UINT depth = 0;
                                spItem->GetDepth(&depth);
                                if (depth > maxDepth)
                                {
                                    maxDepth = depth;
                                }
                            }
                        }

                        // Creating a vector of vectors of items of the same depth
                        // Size by maxDepth+1 (not itemCount) to avoid excessive memory allocation
                        // Cast to size_t before arithmetic to avoid overflow on 32-bit UINT
                        std::vector<std::vector<UINT>> matrix(static_cast<size_t>(maxDepth) + 1);

                        for (UINT u = 0; u < itemCount; u++)
                        {
                            CComPtr<IPowerRenameItem> spItem;
                            if (SUCCEEDED(pwtd->spsrm->GetItemByIndex(u, &spItem)))
                            {
                                UINT depth = 0;
                                spItem->GetDepth(&depth);
                                matrix[depth].push_back(u);
                            }
                        }

                        // Collect pending item updates to be applied after the file operation
                        // succeeds. Updating item data before PerformOperations() would leave
                        // items in a stale "renamed" state if the operation is cancelled,
                        // duplicated, or fails, causing incorrect originals on the next open.
                        struct PendingItemUpdate
                        {
                            CComPtr<IPowerRenameItem> spItem;
                            std::wstring newName;
                            std::wstring newPath;
                            std::wstring oldPath;
                            size_t oldPathSize;
                            size_t operationIndex;
                            int id;
                            bool isFolder;
                        };
                        std::vector<PendingItemUpdate> pendingUpdates;

                        RenameProgressSink* renameProgressSink = new (std::nothrow) RenameProgressSink();
                        CComPtr<IFileOperationProgressSink> spRenameProgressSink;
                        if (renameProgressSink != nullptr)
                        {
                            spRenameProgressSink.Attach(renameProgressSink);
                        }

                        size_t queuedRenameIndex = 0;

                        // From the greatest depth first, add all items of that depth to the operation
                        for (LONG v = static_cast<LONG>(maxDepth); v >= 0; v--)
                        {
                            for (auto it : matrix[v])
                            {
                                CComPtr<IPowerRenameItem> spItem;
                                if (SUCCEEDED(pwtd->spsrm->GetItemByIndex(it, &spItem)))
                                {
                                    bool shouldRename = false;
                                    if (SUCCEEDED(spItem->ShouldRenameItem(flags, &shouldRename)) && shouldRename)
                                    {
                                        PWSTR newName = nullptr;
                                        if (SUCCEEDED(spItem->GetNewName(&newName)))
                                        {
                                            CComPtr<IShellItem> spShellItem;
                                            if (SUCCEEDED(spItem->GetShellItem(&spShellItem)))
                                            {
                                                HRESULT renameHr = spFileOp->RenameItem(spShellItem, newName, nullptr);
                                                const size_t operationIndex = queuedRenameIndex;
                                                if (SUCCEEDED(renameHr))
                                                {
                                                    queuedRenameIndex++;
                                                }

                                                if (!closeUIWindowAfterRenaming && SUCCEEDED(renameHr))
                                                {
                                                    // Collect item data for post-operation update.
                                                    // Both pointers are initialized to nullptr so
                                                    // CoTaskMemFree at the end of this block is safe
                                                    // even when short-circuit evaluation prevents
                                                    // GetPath from being called (CoTaskMemFree(nullptr)
                                                    // is a documented no-op).
                                                    PWSTR originalName = nullptr;
                                                    PWSTR path = nullptr;
                                                    const bool gotNames = SUCCEEDED(spItem->GetOriginalName(&originalName)) &&
                                                                          SUCCEEDED(spItem->GetPath(&path));
                                                    if (gotNames)
                                                    {
                                                        std::wstring originalNameStr{ originalName };
                                                        std::wstring oldPathStr{ path };
                                                        std::wstring pathStr{ oldPathStr };
                                                        size_t oldPathSize = oldPathStr.size();

                                                        auto fileNamePos = pathStr.find_last_of(L"\\");
                                                        pathStr.replace(fileNamePos + 1, originalNameStr.length(), std::wstring{ newName });

                                                        bool isFolder = false;
                                                        if (FAILED(spItem->GetIsFolder(&isFolder)))
                                                        {
                                                            isFolder = false;
                                                        }

                                                        int id = -1;
                                                        if (SUCCEEDED(spItem->GetId(&id)))
                                                        {
                                                            pendingUpdates.emplace_back(PendingItemUpdate{ spItem, std::wstring{ newName }, std::move(pathStr), std::move(oldPathStr), oldPathSize, operationIndex, id, isFolder });
                                                        }
                                                    }
                                                    // Free outside the if block; CoTaskMemFree(nullptr) is safe.
                                                    CoTaskMemFree(originalName);
                                                    CoTaskMemFree(path);
                                                }
                                            }
                                            CoTaskMemFree(newName);
                                        }
                                    }
                                }
                            }
                        }

                        // Set the operation flags
                        if (SUCCEEDED(spFileOp->SetOperationFlags(FOF_DEFAULTFLAGS)))
                        {
                            DWORD fileOpAdviseCookie = 0;
                            if (spRenameProgressSink != nullptr)
                            {
                                spFileOp->Advise(spRenameProgressSink, &fileOpAdviseCookie);
                            }

                            // Set the parent window
                            if (pwtd->hwndParent)
                            {
                                spFileOp->SetOwnerWindow(pwtd->hwndParent);
                            }

                            // Perform the operation
                            HRESULT performHr = spFileOp->PerformOperations();

                            // Update item data only for operations that the shell reports as
                            // successfully renamed. This captures collision-resolved names such
                            // as "bar (2).txt" and avoids stale state for failed items.
                            UNREFERENCED_PARAMETER(performHr);
                            if (renameProgressSink != nullptr)
                            {
                                for (auto& update : pendingUpdates)
                                {
                                    std::wstring actualNewPath;
                                    if (!renameProgressSink->TryGetSuccessfulRename(update.operationIndex, actualNewPath))
                                    {
                                        const bool hasDuplicatePredictedPath = std::count_if(pendingUpdates.begin(), pendingUpdates.end(), [&update](const PendingItemUpdate& other) {
                                            return CompareStringOrdinal(update.newPath.c_str(), -1, other.newPath.c_str(), -1, TRUE) == CSTR_EQUAL;
                                        }) > 1;

                                        if (!hasDuplicatePredictedPath && fs::exists(update.newPath))
                                        {
                                            actualNewPath = update.newPath;
                                        }
                                        else if (!hasDuplicatePredictedPath)
                                        {
                                            for (auto& parentUpdate : pendingUpdates)
                                            {
                                                if (!parentUpdate.isFolder || !StartsWithPathIgnoringCase(update.oldPath, parentUpdate.oldPath))
                                                {
                                                    continue;
                                                }

                                                std::wstring parentNewPath;
                                                if (!renameProgressSink->TryGetSuccessfulRename(parentUpdate.operationIndex, parentNewPath) && fs::exists(parentUpdate.newPath))
                                                {
                                                    parentNewPath = parentUpdate.newPath;
                                                }

                                                if (!parentNewPath.empty())
                                                {
                                                    std::wstring resolvedPath = parentNewPath + update.newPath.substr(parentUpdate.oldPath.size());
                                                    if (fs::exists(resolvedPath))
                                                    {
                                                        actualNewPath = std::move(resolvedPath);
                                                        break;
                                                    }
                                                }
                                            }
                                        }

                                        if (actualNewPath.empty())
                                        {
                                            continue;
                                        }
                                    }

                                    update.spItem->PutPath(actualNewPath.c_str());
                                    update.spItem->PutOriginalName(fs::path(actualNewPath).filename().c_str());
                                    update.spItem->PutNewName(nullptr);

                                    if (update.isFolder)
                                    {
                                        pwtd->spsrm->UpdateChildrenPath(update.id, update.oldPathSize);
                                    }

                                    PostMessage(pwtd->hwndManager, SRM_REGEX_ITEM_RENAMED_KEEP_UI, GetCurrentThreadId(), update.id);
                                }
                            }

                            if (fileOpAdviseCookie != 0)
                            {
                                spFileOp->Unadvise(fileOpAdviseCookie);
                            }
                        }
                    }
                }
            }

            // Send the manager thread the completion message
            PostMessage(pwtd->hwndManager, SRM_FILEOP_COMPLETE, GetCurrentThreadId(), 0);

            delete pwtd;
        }
        CoUninitialize();
    }

    return 0;
}

HRESULT CPowerRenameManager::_PerformRegExRename()
{
    HRESULT hr = E_FAIL;

    if (!TryEnterCriticalSection(&m_critsecReentrancy))
    {
        // Ensure we do not re-enter since we pump messages here.
        // TODO: If we do, post a message back to ourselves
    }
    else
    {
        // Ensure previous thread is canceled
        _CancelRegExWorkerThread();

        // Create worker thread which will message us progress and completion.
        hr = _CreateRegExWorkerThread();
        if (SUCCEEDED(hr))
        {
            ResetEvent(m_cancelRegExWorkerEvent);

            // Signal the worker thread that they can start working. We needed to wait until we
            // were ready to process thread messages.
            SetEvent(m_startRegExWorkerEvent);
        }
    }

    return hr;
}

HRESULT CPowerRenameManager::_CreateRegExWorkerThread()
{
    WorkerThreadData* pwtd = new WorkerThreadData;
    HRESULT hr = E_OUTOFMEMORY;
    if (pwtd)
    {
        pwtd->hwndManager = m_hwndMessage;
        pwtd->startEvent = m_startRegExWorkerEvent;
        pwtd->cancelEvent = m_cancelRegExWorkerEvent;
        pwtd->hwndParent = m_hwndParent;
        pwtd->spsrm = this;
        m_regExWorkerThreadHandle = CreateThread(nullptr, 0, s_regexWorkerThread, pwtd, 0, nullptr);
        hr = E_FAIL;
        if (m_regExWorkerThreadHandle)
        {
            hr = S_OK;
        }
        else
        {
            delete pwtd;
        }
    }

    return hr;
}

DWORD WINAPI CPowerRenameManager::s_regexWorkerThread(_In_ void* pv)
{
    try
    {
        winrt::check_hresult(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE));
        WorkerThreadData* pwtd = static_cast<WorkerThreadData*>(pv);
        if (pwtd)
        {
            PostMessage(pwtd->hwndManager, SRM_REGEX_STARTED, GetCurrentThreadId(), 0);

            // Wait to be told we can begin
            if (WaitForSingleObject(pwtd->startEvent, INFINITE) == WAIT_OBJECT_0)
            {
                CComPtr<IPowerRenameRegEx> spRenameRegEx;

                winrt::check_hresult(pwtd->spsrm->GetRenameRegEx(&spRenameRegEx));

                UINT itemCount = 0;
                unsigned long itemEnumIndex = 0;
                winrt::check_hresult(pwtd->spsrm->GetItemCount(&itemCount));

                for (UINT u = 0; u < itemCount; u++)
                {
                    // Check if cancel event is signaled
                    if (WaitForSingleObject(pwtd->cancelEvent, 0) == WAIT_OBJECT_0)
                    {
                        // Canceled from manager
                        // Send the manager thread the canceled message
                        PostMessage(pwtd->hwndManager, SRM_REGEX_CANCELED, GetCurrentThreadId(), 0);
                        break;
                    }

                    CComPtr<IPowerRenameItem> spItem;
                    winrt::check_hresult(pwtd->spsrm->GetItemByIndex(u, &spItem));

                    DoRename(spRenameRegEx, itemEnumIndex, spItem);
                }
            }

            // Send the manager thread the completion message
            PostMessage(pwtd->hwndManager, SRM_REGEX_COMPLETE, GetCurrentThreadId(), 0);

            delete pwtd;
        }
        CoUninitialize();
    }
    catch (...)
    {
        // TODO: an exception can happen while typing the expression and the syntax is not correct yet,
        // we need to be more granular and raise an exception only when a real problem happened.
        // MessageBox(NULL, L"RegexWorkerThread failed to execute.\nPlease report the bug to https://aka.ms/powerToysReportBug", L"PowerRename Error", MB_OK);
    }

    return 0;
}

void CPowerRenameManager::_CancelRegExWorkerThread()
{
    if (m_startRegExWorkerEvent)
    {
        SetEvent(m_startRegExWorkerEvent);
    }

    if (m_cancelRegExWorkerEvent)
    {
        SetEvent(m_cancelRegExWorkerEvent);
    }

    _WaitForRegExWorkerThread();
}

void CPowerRenameManager::_WaitForRegExWorkerThread()
{
    if (m_regExWorkerThreadHandle)
    {
        WaitForSingleObject(m_regExWorkerThreadHandle, INFINITE);
        CloseHandle(m_regExWorkerThreadHandle);
        m_regExWorkerThreadHandle = nullptr;
    }
}

void CPowerRenameManager::_Cancel()
{
    SetEvent(m_startFileOpWorkerEvent);
    _CancelRegExWorkerThread();
}

HRESULT CPowerRenameManager::_EnsureRegEx()
{
    HRESULT hr = S_OK;
    if (!m_spRegEx)
    {
        // Create the default regex handler
        hr = CPowerRenameRegEx::s_CreateInstance(&m_spRegEx);
        if (SUCCEEDED(hr))
        {
            hr = _InitRegEx();
            // Get the flags
            if (SUCCEEDED(hr))
            {
                m_spRegEx->GetFlags(&m_flags);
            }
        }
    }
    return hr;
}

HRESULT CPowerRenameManager::_InitRegEx()
{
    HRESULT hr = E_FAIL;
    if (m_spRegEx)
    {
        hr = m_spRegEx->Advise(this, &m_regExAdviseCookie);
    }

    return hr;
}

void CPowerRenameManager::_ClearRegEx()
{
    if (m_spRegEx)
    {
        m_spRegEx->UnAdvise(m_regExAdviseCookie);
        m_regExAdviseCookie = 0;
    }
}

void CPowerRenameManager::_OnRename(_In_ IPowerRenameItem* renameItem)
{
    CSRWSharedAutoLock lock(&m_lockEvents);

    for (auto it : m_powerRenameManagerEvents)
    {
        if (it.pEvents)
        {
            it.pEvents->OnRename(renameItem);
        }
    }
}

void CPowerRenameManager::_OnError(_In_ IPowerRenameItem* renameItem)
{
    CSRWSharedAutoLock lock(&m_lockEvents);

    for (auto it : m_powerRenameManagerEvents)
    {
        if (it.pEvents)
        {
            it.pEvents->OnError(renameItem);
        }
    }
}

void CPowerRenameManager::_OnRegExStarted(_In_ DWORD threadId)
{
    CSRWSharedAutoLock lock(&m_lockEvents);

    for (auto it : m_powerRenameManagerEvents)
    {
        if (it.pEvents)
        {
            it.pEvents->OnRegExStarted(threadId);
        }
    }
}

void CPowerRenameManager::_OnRegExCanceled(_In_ DWORD threadId)
{
    CSRWSharedAutoLock lock(&m_lockEvents);

    for (auto it : m_powerRenameManagerEvents)
    {
        if (it.pEvents)
        {
            it.pEvents->OnRegExCanceled(threadId);
        }
    }
}

void CPowerRenameManager::_OnRegExCompleted(_In_ DWORD threadId)
{
    CSRWSharedAutoLock lock(&m_lockEvents);

    for (auto it : m_powerRenameManagerEvents)
    {
        if (it.pEvents)
        {
            it.pEvents->OnRegExCompleted(threadId);
        }
    }
}

void CPowerRenameManager::_OnRenameStarted()
{
    CSRWSharedAutoLock lock(&m_lockEvents);

    for (auto it : m_powerRenameManagerEvents)
    {
        if (it.pEvents)
        {
            it.pEvents->OnRenameStarted();
        }
    }
}

void CPowerRenameManager::_OnRenameCompleted()
{
    CSRWSharedAutoLock lock(&m_lockEvents);

    for (auto it : m_powerRenameManagerEvents)
    {
        if (it.pEvents)
        {
            it.pEvents->OnRenameCompleted(m_closeUIWindowAfterRenaming);
        }
    }
}

void CPowerRenameManager::_ClearEventHandlers()
{
    CSRWExclusiveAutoLock lock(&m_lockEvents);

    // Cleanup event handlers
    for (std::vector<RENAME_MGR_EVENT>::iterator it = m_powerRenameManagerEvents.begin(); it != m_powerRenameManagerEvents.end(); ++it)
    {
        it->cookie = 0;
        if (it->pEvents)
        {
            it->pEvents->Release();
            it->pEvents = nullptr;
        }
    }

    m_powerRenameManagerEvents.clear();
}

void CPowerRenameManager::_ClearPowerRenameItems()
{
    CSRWExclusiveAutoLock lock(&m_lockItems);

    // Cleanup rename items
    for (std::map<int, IPowerRenameItem*>::iterator it = m_renameItems.begin(); it != m_renameItems.end(); ++it)
    {
        IPowerRenameItem* pItem = it->second;
        if (pItem)
        {
            pItem->Release();
            it->second = nullptr;
        }
    }

    m_renameItems.clear();
}

void CPowerRenameManager::_Cleanup()
{
    if (m_hwndMessage)
    {
        DestroyWindow(m_hwndMessage);
        m_hwndMessage = nullptr;
    }

    CloseHandle(m_startFileOpWorkerEvent);
    m_startFileOpWorkerEvent = nullptr;

    CloseHandle(m_startRegExWorkerEvent);
    m_startRegExWorkerEvent = nullptr;

    CloseHandle(m_cancelRegExWorkerEvent);
    m_cancelRegExWorkerEvent = nullptr;

    _ClearRegEx();
    _ClearEventHandlers();
    _ClearPowerRenameItems();
}
