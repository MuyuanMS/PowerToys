#include "pch.h"
#include "unhandled_exception_handler.h"
#include <common/logger/logger.h>
#include <csignal>
#include <cstdio>
#include <string>

#if _DEBUG && _WIN64
#include <DbgHelp.h>
#pragma comment(lib, "DbgHelp.lib")
#include <sstream>
#endif

static bool processing_exception = false;
static LPTOP_LEVEL_EXCEPTION_FILTER default_top_level_exception_handler = nullptr;

// Pre-opened crash-marker file handle.  Opened once in init_global_error_handlers()
// before any potentially-throwing startup code.  WriteFile to this handle is
// allocation-free and mutex-free so it is safe to use from the SEH filter and the
// SIGABRT handler.
static HANDLE g_crash_marker_handle = INVALID_HANDLE_VALUE;

// Write a null-terminated ASCII message to the crash marker file.
// Must not allocate memory, lock mutexes, or throw.
static void write_crash_marker(const char* msg) noexcept
{
    if (g_crash_marker_handle == INVALID_HANDLE_VALUE || msg == nullptr)
    {
        return;
    }
    DWORD len = 0;
    while (msg[len] != '\0')
    {
        ++len;
    }
    if (len > 0)
    {
        DWORD written = 0;
        WriteFile(g_crash_marker_handle, msg, len, &written, nullptr);
    }
}

void write_crash_marker_message(const char* msg)
{
    write_crash_marker(msg);
}

static const WCHAR* exception_description(const DWORD& code)
{
    switch (code)
    {
    case EXCEPTION_ACCESS_VIOLATION:
        return L"EXCEPTION_ACCESS_VIOLATION";
    case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:
        return L"EXCEPTION_ARRAY_BOUNDS_EXCEEDED";
    case EXCEPTION_BREAKPOINT:
        return L"EXCEPTION_BREAKPOINT";
    case EXCEPTION_DATATYPE_MISALIGNMENT:
        return L"EXCEPTION_DATATYPE_MISALIGNMENT";
    case EXCEPTION_FLT_DENORMAL_OPERAND:
        return L"EXCEPTION_FLT_DENORMAL_OPERAND";
    case EXCEPTION_FLT_DIVIDE_BY_ZERO:
        return L"EXCEPTION_FLT_DIVIDE_BY_ZERO";
    case EXCEPTION_FLT_INEXACT_RESULT:
        return L"EXCEPTION_FLT_INEXACT_RESULT";
    case EXCEPTION_FLT_INVALID_OPERATION:
        return L"EXCEPTION_FLT_INVALID_OPERATION";
    case EXCEPTION_FLT_OVERFLOW:
        return L"EXCEPTION_FLT_OVERFLOW";
    case EXCEPTION_FLT_STACK_CHECK:
        return L"EXCEPTION_FLT_STACK_CHECK";
    case EXCEPTION_FLT_UNDERFLOW:
        return L"EXCEPTION_FLT_UNDERFLOW";
    case EXCEPTION_ILLEGAL_INSTRUCTION:
        return L"EXCEPTION_ILLEGAL_INSTRUCTION";
    case EXCEPTION_IN_PAGE_ERROR:
        return L"EXCEPTION_IN_PAGE_ERROR";
    case EXCEPTION_INT_DIVIDE_BY_ZERO:
        return L"EXCEPTION_INT_DIVIDE_BY_ZERO";
    case EXCEPTION_INT_OVERFLOW:
        return L"EXCEPTION_INT_OVERFLOW";
    case EXCEPTION_INVALID_DISPOSITION:
        return L"EXCEPTION_INVALID_DISPOSITION";
    case EXCEPTION_NONCONTINUABLE_EXCEPTION:
        return L"EXCEPTION_NONCONTINUABLE_EXCEPTION";
    case EXCEPTION_PRIV_INSTRUCTION:
        return L"EXCEPTION_PRIV_INSTRUCTION";
    case EXCEPTION_SINGLE_STEP:
        return L"EXCEPTION_SINGLE_STEP";
    case EXCEPTION_STACK_OVERFLOW:
        return L"EXCEPTION_STACK_OVERFLOW";
    default:
        return L"UNKNOWN EXCEPTION";
    }
}

#if _DEBUG && _WIN64
static IMAGEHLP_SYMBOL64* p_symbol = static_cast<IMAGEHLP_SYMBOL64*>(malloc(sizeof(IMAGEHLP_SYMBOL64) + MAX_PATH * sizeof(WCHAR)));
static IMAGEHLP_LINE64 line;
static WCHAR module_path[MAX_PATH];

void init_symbols()
{
    SymSetOptions(SYMOPT_LOAD_LINES | SYMOPT_UNDNAME);
    line.SizeOfStruct = sizeof(IMAGEHLP_LINE64);
    auto process = GetCurrentProcess();
    SymInitialize(process, NULL, TRUE);
}

void log_stack_trace(std::wstring& generalErrorDescription)
{
    memset(p_symbol, '\0', sizeof(*p_symbol) + MAX_PATH);
    memset(&module_path[0], '\0', sizeof(module_path));
    line.LineNumber = 0;

    CONTEXT context;
    RtlCaptureContext(&context);
    auto process = GetCurrentProcess();
    auto thread = GetCurrentThread();
    STACKFRAME64 stack;
    memset(&stack, 0, sizeof(STACKFRAME64));

#ifdef _M_ARM64
    stack.AddrPC.Offset = context.Pc;
    stack.AddrStack.Offset = context.Sp;
    stack.AddrFrame.Offset = context.Fp;
#else
    stack.AddrPC.Offset = context.Rip;
    stack.AddrStack.Offset = context.Rsp;
    stack.AddrFrame.Offset = context.Rbp;
#endif
    stack.AddrPC.Mode = AddrModeFlat;
    stack.AddrStack.Mode = AddrModeFlat;
    stack.AddrFrame.Mode = AddrModeFlat;

    std::wstringstream ss;
    ss << generalErrorDescription << std::endl;
    for (ULONG frame = 0;; frame++)
    {
        auto result = StackWalk64(
#ifdef _M_ARM64
            IMAGE_FILE_MACHINE_ARM64,
#else
            IMAGE_FILE_MACHINE_AMD64,
#endif
            process,
            thread,
            &stack,
            &context,
            NULL,
            SymFunctionTableAccess64,
            SymGetModuleBase64,
            NULL);

        p_symbol->MaxNameLength = MAX_PATH;
        p_symbol->SizeOfStruct = sizeof(IMAGEHLP_SYMBOL64);

        DWORD64 dw64Displacement;
        SymGetSymFromAddr64(process, stack.AddrPC.Offset, &dw64Displacement, p_symbol);
        DWORD dwDisplacement;
        SymGetLineFromAddr64(process, stack.AddrPC.Offset, &dwDisplacement, &line);

        auto module_base = SymGetModuleBase64(process, stack.AddrPC.Offset);
        if (module_base)
        {
            GetModuleFileName(reinterpret_cast<HINSTANCE>(module_base), module_path, MAX_PATH);
        }
        ss << module_path << "!"
           << p_symbol->Name
           << "(" << line.FileName << ":" << line.LineNumber << ")\n";
        if (!result)
        {
            break;
        }
    }
    auto errorString = ss.str();
    MessageBoxW(NULL, errorString.c_str(), L"Unhandled Error", MB_OK | MB_ICONERROR);
}
#endif // _DEBUG && _WIN64

LONG WINAPI unhandled_exception_handler(PEXCEPTION_POINTERS info)
{
    if (!processing_exception)
    {
        processing_exception = true;
        DWORD code = 0;
        if (info != nullptr && info->ExceptionRecord != nullptr)
        {
            code = info->ExceptionRecord->ExceptionCode;
        }
        // Primary: allocation-free, mutex-free persistent marker.  Works even before
        // Logger is initialised and even if the heap or spdlog internals are corrupted.
        char crashMsg[128];
        int msgLen = _snprintf_s(crashMsg, sizeof(crashMsg), _TRUNCATE,
                                 "[PowerToys Runner] CRASH: Unhandled exception code=0x%08X\n",
                                 static_cast<unsigned int>(code));
        if (msgLen > 0)
        {
            write_crash_marker(crashMsg);
        }
        // Best-effort: full Logger path (may fail/deadlock if the fault holds a spdlog lock).
        try
        {
            Logger::critical(L"Runner crashed with unhandled exception: {} (code: 0x{:08X})", exception_description(code), static_cast<unsigned int>(code));
            Logger::flush();
#if _DEBUG && _WIN64
            init_symbols();
            std::wstring ex_description = (info != nullptr && info->ExceptionRecord != nullptr) ? std::wstring{ exception_description(code) } : L"Exception code not available";
            log_stack_trace(ex_description);
#endif
        }
        catch (...)
        {
        }
        // Keep the recursion guard SET while invoking the previous handler.  Clearing it
        // first would allow infinite re-entry if that filter raises a nested exception.
        // Return its disposition so an existing crash reporter can still take control.
        if (default_top_level_exception_handler != nullptr && info != nullptr)
        {
            return default_top_level_exception_handler(info);
        }
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

extern "C" void AbortHandler(int /*signal_number*/)
{
    // Logger is NOT called here: its sinks lock mutexes and allocate memory, which can
    // deadlock or fault again if abort() fires while those resources are held.
    // The pre-opened crash-marker handle gives a persistent, allocation-free on-disk record.
    static const char k_abort_msg[] = "[PowerToys Runner] CRASH: SIGABRT received (abort/assert failure).\n";
    write_crash_marker(k_abort_msg);
    OutputDebugStringW(L"[PowerToys Runner] SIGABRT received (abort/assert failure).\n");
#if _DEBUG && _WIN64
    init_symbols();
    std::wstring ex_description = L"SIGABRT was raised.";
    log_stack_trace(ex_description);
#endif
}

void init_global_error_handlers()
{
    // Open a crash-marker file before registering the handlers so the SEH filter and
    // SIGABRT handler can write an allocation-free diagnostic to disk even before
    // Logger::init() has been called.  GetEnvironmentVariableW requires no COM and is
    // safe at the earliest startup point.
    wchar_t localAppData[MAX_PATH];
    DWORD envLen = GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH);
    if (envLen > 0 && envLen < MAX_PATH)
    {
        wchar_t dirPath[MAX_PATH];
        if (_snwprintf_s(dirPath, MAX_PATH, _TRUNCATE,
                         L"%s\\Microsoft\\PowerToys", localAppData) > 0)
        {
            CreateDirectoryW(dirPath, nullptr); // no-op if the directory already exists
            wchar_t crashPath[MAX_PATH];
            if (_snwprintf_s(crashPath, MAX_PATH, _TRUNCATE,
                             L"%s\\runner-crash.log", dirPath) > 0)
            {
                g_crash_marker_handle = CreateFileW(
                    crashPath,
                    GENERIC_WRITE,
                    FILE_SHARE_READ,
                    nullptr,
                    OPEN_ALWAYS,
                    FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH,
                    nullptr);
            }
        }
    }

    default_top_level_exception_handler = SetUnhandledExceptionFilter(unhandled_exception_handler);
    signal(SIGABRT, &AbortHandler);
}

