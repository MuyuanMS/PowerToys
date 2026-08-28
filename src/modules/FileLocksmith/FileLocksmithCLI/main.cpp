#include "pch.h"
#include "CLILogic.h"
#include "FileLocksmithLib/FileLocksmith.h"
#include "FileLocksmithLib/Trace.h"
#include <common/utils/json.h>
#include <chrono>
#include <iostream>
#include <iterator>
#include <optional>
#include <stdexcept>
#include "resource.h"
#include <common/logger/logger.h>
#include <common/utils/logger_helper.h>

struct RealProcessFinder : IProcessFinder
{
    std::vector<ProcessResult> find(const std::vector<std::wstring>& paths) override
    {
        return find_processes_recursive(paths);
    }
};

struct RealProcessTerminator : IProcessTerminator
{
    bool terminate(DWORD pid) override
    {
        HANDLE hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, pid);
        if (hProcess)
        {
            bool result = TerminateProcess(hProcess, 0);
            CloseHandle(hProcess);
            return result;
        }
        return false;
    }
};

struct RealStringProvider : IStringProvider
{
    std::wstring GetString(UINT id) override
    {
        wchar_t buffer[4096];
        int len = LoadStringW(GetModuleHandle(NULL), id, buffer, ARRAYSIZE(buffer));
        if (len > 0)
        {
            return std::wstring(buffer, len);
        }
        return L"";
    }
};

namespace
{
    constexpr std::wstring_view WorkerArgument = L"--worker-json";
    constexpr DWORD DefaultWorkerTimeoutMilliseconds = 30000;

    DWORD worker_timeout_milliseconds()
    {
#ifdef _DEBUG
        wchar_t value[16]{};
        if (GetEnvironmentVariableW(L"POWERTOYS_FILE_LOCKSMITH_TEST_TIMEOUT_MS", value, ARRAYSIZE(value)) > 0)
        {
            wchar_t* end = nullptr;
            const auto timeout = wcstoul(value, &end, 10);
            if (end != value && *end == L'\0' && timeout > 0)
            {
                return static_cast<DWORD>(timeout);
            }
        }
#endif
        return DefaultWorkerTimeoutMilliseconds;
    }

    struct unique_handle
    {
        unique_handle() = default;

        explicit unique_handle(HANDLE value) :
            value(value)
        {
        }

        ~unique_handle()
        {
            reset();
        }

        unique_handle(const unique_handle&) = delete;
        unique_handle& operator=(const unique_handle&) = delete;

        HANDLE get() const
        {
            return value;
        }

        explicit operator bool() const
        {
            return value && value != INVALID_HANDLE_VALUE;
        }

        void reset(HANDLE replacement = nullptr)
        {
            if (*this)
            {
                CloseHandle(value);
            }
            value = replacement;
        }

    private:
        HANDLE value = nullptr;
    };

    std::string create_worker_request(const std::vector<std::wstring>& paths)
    {
        json::JsonArray json_paths;
        for (const auto& path : paths)
        {
            json_paths.Append(json::JsonValue::CreateStringValue(path));
        }

        json::JsonObject request;
        request.SetNamedValue(L"paths", json_paths);
        return winrt::to_string(request.Stringify());
    }

    std::optional<std::string> run_isolated_worker(const std::vector<std::wstring>& paths)
    {
        SECURITY_ATTRIBUTES inheritable_attributes{ sizeof(inheritable_attributes), nullptr, TRUE };
        HANDLE child_stdin_raw = nullptr;
        HANDLE parent_stdin_raw = nullptr;
        HANDLE parent_stdout_raw = nullptr;
        HANDLE child_stdout_raw = nullptr;
        if (!CreatePipe(&child_stdin_raw, &parent_stdin_raw, &inheritable_attributes, 0) ||
            !CreatePipe(&parent_stdout_raw, &child_stdout_raw, &inheritable_attributes, 0))
        {
            if (child_stdin_raw)
            {
                CloseHandle(child_stdin_raw);
            }
            if (parent_stdin_raw)
            {
                CloseHandle(parent_stdin_raw);
            }
            if (parent_stdout_raw)
            {
                CloseHandle(parent_stdout_raw);
            }
            if (child_stdout_raw)
            {
                CloseHandle(child_stdout_raw);
            }
            return std::nullopt;
        }

        unique_handle child_stdin{ child_stdin_raw };
        unique_handle parent_stdin{ parent_stdin_raw };
        unique_handle parent_stdout{ parent_stdout_raw };
        unique_handle child_stdout{ child_stdout_raw };
        unique_handle child_stderr{
            CreateFileW(
                L"NUL",
                GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                &inheritable_attributes,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                nullptr)
        };
        if (!SetHandleInformation(parent_stdin.get(), HANDLE_FLAG_INHERIT, 0) ||
            !SetHandleInformation(parent_stdout.get(), HANDLE_FLAG_INHERIT, 0) ||
            !child_stderr)
        {
            return std::nullopt;
        }

        unique_handle job{ CreateJobObjectW(nullptr, nullptr) };
        if (!job)
        {
            return std::nullopt;
        }

        JOBOBJECT_EXTENDED_LIMIT_INFORMATION job_information{};
        job_information.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(
                job.get(),
                JobObjectExtendedLimitInformation,
                &job_information,
                sizeof(job_information)))
        {
            return std::nullopt;
        }

        std::wstring executable_path(MAX_PATH, L'\0');
        const auto executable_length = GetModuleFileNameW(
            nullptr,
            executable_path.data(),
            static_cast<DWORD>(executable_path.size()));
        if (executable_length == 0 || executable_length == executable_path.size())
        {
            return std::nullopt;
        }
        executable_path.resize(executable_length);

        std::wstring command_line = L"\"" + executable_path + L"\" " + std::wstring(WorkerArgument);
        STARTUPINFOW startup_info{ sizeof(startup_info) };
        startup_info.dwFlags = STARTF_USESTDHANDLES;
        startup_info.hStdInput = child_stdin.get();
        startup_info.hStdOutput = child_stdout.get();
        startup_info.hStdError = child_stderr.get();

        PROCESS_INFORMATION process_info{};
        if (!CreateProcessW(
                executable_path.c_str(),
                command_line.data(),
                nullptr,
                nullptr,
                TRUE,
                CREATE_NO_WINDOW | CREATE_SUSPENDED,
                nullptr,
                nullptr,
                &startup_info,
                &process_info))
        {
            return std::nullopt;
        }

        unique_handle process{ process_info.hProcess };
        unique_handle thread{ process_info.hThread };
        const auto terminate_worker = [&] {
            if (TerminateJobObject(job.get(), 2))
            {
                WaitForSingleObject(process.get(), INFINITE);
            }
        };
        if (!AssignProcessToJobObject(job.get(), process.get()))
        {
            if (!TerminateProcess(process.get(), 2))
            {
                ResumeThread(thread.get());
            }
            WaitForSingleObject(process.get(), INFINITE);
            return std::nullopt;
        }

        if (ResumeThread(thread.get()) == static_cast<DWORD>(-1))
        {
            terminate_worker();
            return std::nullopt;
        }

        child_stdin.reset();
        child_stdout.reset();

        const auto request = create_worker_request(paths);
        size_t written_total = 0;
        while (written_total < request.size())
        {
            DWORD written = 0;
            if (!WriteFile(
                    parent_stdin.get(),
                    request.data() + written_total,
                    static_cast<DWORD>(request.size() - written_total),
                    &written,
                    nullptr) ||
                written == 0)
            {
                terminate_worker();
                return std::nullopt;
            }
            written_total += written;
        }
        parent_stdin.reset();

        std::string output;
        const auto deadline = GetTickCount64() + worker_timeout_milliseconds();
        while (GetTickCount64() < deadline)
        {
            DWORD available = 0;
            if (!PeekNamedPipe(parent_stdout.get(), nullptr, 0, nullptr, &available, nullptr))
            {
                if (GetLastError() != ERROR_BROKEN_PIPE)
                {
                    terminate_worker();
                    return std::nullopt;
                }
                available = 0;
            }

            while (available > 0)
            {
                char buffer[4096];
                DWORD read = 0;
                const auto to_read = (std::min)(available, static_cast<DWORD>(sizeof(buffer)));
                if (!ReadFile(parent_stdout.get(), buffer, to_read, &read, nullptr))
                {
                    terminate_worker();
                    return std::nullopt;
                }
                output.append(buffer, read);
                available -= read;
            }

            if (WaitForSingleObject(process.get(), 0) == WAIT_OBJECT_0)
            {
                for (;;)
                {
                    char buffer[4096];
                    DWORD read = 0;
                    if (!ReadFile(parent_stdout.get(), buffer, sizeof(buffer), &read, nullptr))
                    {
                        if (GetLastError() != ERROR_BROKEN_PIPE)
                        {
                            return std::nullopt;
                        }
                        break;
                    }
                    output.append(buffer, read);
                }

                DWORD exit_code = 0;
                if (!GetExitCodeProcess(process.get(), &exit_code) || exit_code != 0)
                {
                    return std::nullopt;
                }
                return output;
            }

            Sleep(10);
        }

        terminate_worker();
        return std::nullopt;
    }

    std::optional<std::vector<ProcessResult>> parse_worker_response(const std::string& output)
    {
        try
        {
            json::JsonObject response;
            if (!json::JsonObject::TryParse(winrt::to_hstring(output), response) ||
                !response.HasKey(L"processes"))
            {
                return std::nullopt;
            }

            std::vector<ProcessResult> results;
            const auto processes = response.GetNamedArray(L"processes");
            results.reserve(processes.Size());
            for (const auto& item : processes)
            {
                if (item.ValueType() != json::JsonValueType::Object)
                {
                    return std::nullopt;
                }

                const auto process = item.GetObject();
                ProcessResult result{
                    process.GetNamedString(L"name").c_str(),
                    static_cast<DWORD>(process.GetNamedNumber(L"pid")),
                    process.GetNamedString(L"user").c_str(),
                    {},
                };

                const auto files = process.GetNamedArray(L"files");
                result.files.reserve(files.Size());
                for (const auto& file : files)
                {
                    if (file.ValueType() != json::JsonValueType::String)
                    {
                        return std::nullopt;
                    }
                    result.files.emplace_back(file.GetString());
                }
                results.push_back(std::move(result));
            }
            return results;
        }
        catch (const winrt::hresult_error&)
        {
            return std::nullopt;
        }
    }

    std::optional<std::vector<std::wstring>> read_worker_paths()
    {
        const std::string input{
            std::istreambuf_iterator<char>{ std::cin },
            std::istreambuf_iterator<char>{}
        };

        try
        {
            json::JsonObject request;
            if (!json::JsonObject::TryParse(winrt::to_hstring(input), request) || !request.HasKey(L"paths"))
            {
                return std::nullopt;
            }

            std::vector<std::wstring> paths;
            const auto json_paths = request.GetNamedArray(L"paths");
            paths.reserve(json_paths.Size());

            for (const auto& path : json_paths)
            {
                if (path.ValueType() != json::JsonValueType::String)
                {
                    return std::nullopt;
                }

                paths.emplace_back(path.GetString());
            }

            if (paths.empty())
            {
                return std::nullopt;
            }

            return paths;
        }
        catch (const winrt::hresult_error&)
        {
            return std::nullopt;
        }
    }

    struct IsolatedProcessFinder : IProcessFinder
    {
        std::vector<ProcessResult> find(const std::vector<std::wstring>& paths) override
        {
            const auto output = run_isolated_worker(paths);
            if (!output)
            {
                throw std::runtime_error("isolated worker failed or timed out");
            }

            auto results = parse_worker_response(*output);
            if (!results)
            {
                throw std::runtime_error("isolated worker returned malformed output");
            }
            return std::move(*results);
        }
    };
}

#ifndef UNIT_TEST
int wmain(int argc, wchar_t* argv[])
{
    winrt::init_apartment();
    Trace::RegisterProvider();
    LoggerHelpers::init_logger(L"FileLocksmithCLI", L"", LogSettings::fileLocksmithLoggerName);
    Logger::info("FileLocksmithCLI started");

    RealProcessTerminator terminator;
    RealStringProvider strings;

    if (argc == 2 && argv[1] == WorkerArgument)
    {
        RealProcessFinder finder;
        const auto paths = read_worker_paths();
        if (!paths)
        {
            Logger::error("Worker input was malformed");
            Trace::CLICommand(L"worker-query", false);
            Trace::UnregisterProvider();
            return 2;
        }

#ifdef _DEBUG
        if (GetEnvironmentVariableW(L"POWERTOYS_FILE_LOCKSMITH_TEST_BLOCK_WORKER", nullptr, 0) > 0)
        {
            Sleep(INFINITE);
        }
#endif

        Logger::info("Worker query started with {} paths", paths->size());
        const auto started = std::chrono::steady_clock::now();
        const auto result = run_worker_query(*paths, finder);
        const auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - started);
        Logger::info("Worker query completed in {} ms with exit code {}", duration.count(), result.exit_code);

        std::cout << winrt::to_string(result.output);
        Trace::CLICommand(result.command_name.c_str(), result.exit_code == 0);
        Trace::UnregisterProvider();
        return result.exit_code;
    }

    IsolatedProcessFinder finder;
    auto result = run_command(argc, argv, finder, terminator, strings);

    if (result.exit_code != 0)
    {
        Logger::error("Command failed with exit code {}", result.exit_code);
    }
    else
    {
        Logger::info("Command succeeded");
    }

    Trace::CLICommand(result.command_name.c_str(), result.exit_code == 0);

    std::wcout << result.output;
    Trace::UnregisterProvider();
    return result.exit_code;
}
#endif
