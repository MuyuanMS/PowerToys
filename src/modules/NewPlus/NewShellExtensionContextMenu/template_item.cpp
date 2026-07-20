#include "pch.h"
#include "template_item.h"
#include <shellapi.h>
#include "new_utilities.h"
#include <cassert>
#include <thread>
#include <shlobj_core.h>
#include <CommCtrl.h>

using namespace Microsoft::WRL;
using namespace newplus;

namespace
{
    constexpr auto rename_mode_startup_timeout = std::chrono::seconds(5);
    constexpr DWORD rename_monitoring_poll_interval_ms = 200;
    constexpr UINT rename_edit_control_query_timeout_ms = 200;

    class background_worker_lifetime_guard
    {
    public:
        explicit background_worker_lifetime_guard(HMODULE module_reference) :
            m_module_reference(module_reference)
        {
        }

        ~background_worker_lifetime_guard()
        {
            if (m_module_reference != nullptr)
            {
                release_background_worker_module_reference(m_module_reference);
            }
        }

        [[noreturn]] void release_and_exit_thread()
        {
            const HMODULE module_reference = m_module_reference;
            m_module_reference = nullptr;
            release_background_worker_module_reference_and_exit_thread(module_reference);
        }

    private:
        HMODULE m_module_reference;
    };

    class com_initialize_guard
    {
    public:
        com_initialize_guard()
            : m_result(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))
        {
        }

        ~com_initialize_guard()
        {
            if (SUCCEEDED(m_result))
            {
                CoUninitialize();
            }
        }

    private:
        HRESULT m_result;
    };

    struct file_identity
    {
        bool valid = false;
        DWORD volume_serial_number = 0;
        DWORD file_index_high = 0;
        DWORD file_index_low = 0;
    };

    file_identity get_file_identity(const std::filesystem::path& path)
    {
        file_identity identity;
        HANDLE file_handle = CreateFileW(
            path.c_str(),
            0,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            nullptr);

        if (file_handle == INVALID_HANDLE_VALUE)
        {
            return identity;
        }

        BY_HANDLE_FILE_INFORMATION file_information{};
        if (GetFileInformationByHandle(file_handle, &file_information) == FALSE)
        {
            CloseHandle(file_handle);
            return identity;
        }

        CloseHandle(file_handle);
        identity.valid = true;
        identity.volume_serial_number = file_information.dwVolumeSerialNumber;
        identity.file_index_high = file_information.nFileIndexHigh;
        identity.file_index_low = file_information.nFileIndexLow;
        return identity;
    }

    bool is_same_file_identity(const file_identity& left, const file_identity& right)
    {
        return left.valid && right.valid &&
               left.volume_serial_number == right.volume_serial_number &&
               left.file_index_high == right.file_index_high &&
               left.file_index_low == right.file_index_low;
    }

    bool try_find_child_by_identity(const std::filesystem::path& parent_dir, const file_identity& target_identity, std::filesystem::path& final_path)
    {
        if (!target_identity.valid)
        {
            return false;
        }

        for (const auto& entry : std::filesystem::directory_iterator(parent_dir))
        {
            if (is_same_file_identity(get_file_identity(entry.path()), target_identity))
            {
                final_path = entry.path();
                return true;
            }
        }

        return false;
    }

    bool is_rename_mode_active(HWND list_view_window);

    bool wait_for_rename_mode_to_finish(HWND list_view_window)
    {
        if (list_view_window == nullptr)
        {
            return false;
        }

        bool saw_rename_edit_control = false;
        const auto monitoring_started = std::chrono::steady_clock::now();

        while (true)
        {
            if (is_rename_mode_active(list_view_window))
            {
                saw_rename_edit_control = true;
            }
            else if (saw_rename_edit_control)
            {
                return true;
            }
            else if (std::chrono::steady_clock::now() - monitoring_started >= rename_mode_startup_timeout)
            {
                return false;
            }

            std::this_thread::sleep_for(std::chrono::milliseconds(rename_monitoring_poll_interval_ms));
        }
    }

    bool process_directory_notifications_for_target_rename(const BYTE* buffer, DWORD bytes_returned, const std::wstring& original_folder_name, const std::filesystem::path& parent_dir, std::filesystem::path& final_path, bool& found_old_name)
    {
        const BYTE* ptr = buffer;
        while (ptr < buffer + bytes_returned)
        {
            const auto* info = reinterpret_cast<const FILE_NOTIFY_INFORMATION*>(ptr);

            if (info->Action == FILE_ACTION_RENAMED_OLD_NAME)
            {
                const std::wstring old_name(info->FileName, info->FileNameLength / sizeof(WCHAR));
                if (StrCmpIW(old_name.c_str(), original_folder_name.c_str()) == 0)
                {
                    found_old_name = true;
                }
            }
            else if (info->Action == FILE_ACTION_RENAMED_NEW_NAME && found_old_name)
            {
                const std::wstring new_name(info->FileName, info->FileNameLength / sizeof(WCHAR));
                final_path = parent_dir / new_name;
                return true;
            }

            if (info->NextEntryOffset == 0)
            {
                break;
            }
            ptr += info->NextEntryOffset;
        }

        return false;
    }

    bool is_rename_mode_active(HWND list_view_window)
    {
        if (list_view_window == nullptr || !IsWindow(list_view_window))
        {
            return false;
        }

        DWORD_PTR edit_control = 0;
        const LRESULT message_result = SendMessageTimeoutW(
            list_view_window,
            LVM_GETEDITCONTROL,
            0,
            0,
            SMTO_ABORTIFHUNG | SMTO_BLOCK,
            rename_edit_control_query_timeout_ms,
            &edit_control);

        return message_result != 0 && reinterpret_cast<HWND>(edit_control) != nullptr;
    }

}

template_item::template_item(const std::filesystem::path entry)
{
    path = entry;
}

std::wstring template_item::get_menu_title(const bool show_extension, const bool show_starting_digits, const bool show_resolved_variables) const
{
    std::wstring title = path.filename();

    if (!show_starting_digits)
    {
        // Hide starting digits, spaces, and .
        title = remove_starting_digits_from_filename(title);
    }

    if (show_resolved_variables)
    {
        title = helpers::variables::resolve_variables_in_filename(title, constants::non_localizable::parent_folder_name_variable);
    }

    if (show_extension || !path.has_extension())
    {
        return title;
    }

    if (!helpers::filesystem::is_directory(path))
    {
        std::wstring ext = path.extension();
        title = title.substr(0, title.length() - ext.length());
    }

    return title;
}

std::wstring template_item::get_target_filename(const bool include_starting_digits) const
{
    std::wstring filename = path.filename();

    if (!include_starting_digits)
    {
        // Remove starting digits, spaces, and .
        filename = remove_starting_digits_from_filename(filename);
    }

    return filename;
}

std::wstring template_item::remove_starting_digits_from_filename(std::wstring filename) const
{
    // Filename cases to support
    // type      | filename                             | result
    // [file]    | 01. First entry.txt                  | First entry.txt
    // [folder]  | 02. Second entry                     | Second entry
    // [folder]  | 03 Third entry                       | Third entry
    // [file]    | 04 Fourth entry.txt                  | Fourth entry.txt
    // [file]    | 05.Fifth entry.txt                   | Fifth entry.txt
    // [folder]  | 001231                               | 001231
    // [file]    | 001231.txt                           | 001231.txt
    // [file]    | 13. 0123456789012345.txt             | 0123456789012345.txt

    std::filesystem::path filename_path(filename);
    const std::wstring stem = filename_path.stem().wstring();

    bool stem_is_only_digits = !stem.empty();
    for (const wchar_t c : stem)
    {
        if (c < L'0' || c > L'9')
        {
            stem_is_only_digits = false;
            break;
        }
    }

    if (stem_is_only_digits)
    {
        // Edge cases where digits ARE the filename.
        // If it's a file, we always keep it (e.g. 001231.txt or 001231).
        // If it's a folder, we only strip if it looks like it has an extension (which is actually part of the name for folders).
        // e.g. "0123.Name" -> Strip. "001231" -> Keep.
        const bool is_folder = helpers::filesystem::is_directory(path);
        const bool has_extension = filename_path.has_extension();

        if (!is_folder || !has_extension)
        {
            return filename;
        }
    }

    // Find end of leading digits
    size_t digits_end_index = 0;
    while (digits_end_index < filename.length() && filename[digits_end_index] >= L'0' && filename[digits_end_index] <= L'9')
    {
        digits_end_index++;
    }

    if (digits_end_index == 0)
    {
        // No leading digits
        return filename;
    }

    // Determine if we should also strip a separator (dot or space)
    size_t strip_length = digits_end_index;

    // Check patterns to strip separators:
    // 1. "01. Name" -> Strip "01. "
    // 2. "01 .Name" -> Strip "01 ."
    // 3. "01.Name"  -> Strip "01."
    // 4. "01 Name"  -> Strip "01 "
    // 5. "01Name"   -> Strip "01" (No separator)

    if (strip_length < filename.length())
    {
        if (filename[strip_length] == L'.')
        {
            strip_length++;
            // If dot is followed by space, strip that too (e.g. "01. Name")
            if (strip_length < filename.length() && filename[strip_length] == L' ')
            {
                strip_length++;
            }
        }
        else if (filename[strip_length] == L' ')
        {
            strip_length++;
            // If space is followed by dot, strip that too (e.g. "01 .Name")
            if (strip_length < filename.length() && filename[strip_length] == L'.')
            {
                strip_length++;
            }
        }
    }

    return filename.substr(strip_length);
}

std::wstring template_item::get_explorer_icon() const
{
    return utilities::get_explorer_icon(path);
}

HICON template_item::get_explorer_icon_handle() const
{
    return utilities::get_explorer_icon_handle(path);
}

std::filesystem::path template_item::copy_object_to(const HWND window_handle, const std::filesystem::path destination) const
{
    // SHFILEOPSTRUCT wants the from and to paths to be terminated with two NULLs.
    wchar_t double_terminated_path_from[MAX_PATH + 1] = { 0 };
    wcsncpy_s(double_terminated_path_from, this->path.c_str(), this->path.wstring().length());
    double_terminated_path_from[this->path.wstring().length() + 1] = 0;

    wchar_t double_terminated_path_to[MAX_PATH + 1] = { 0 };
    wcsncpy_s(double_terminated_path_to, destination.c_str(), destination.wstring().length());
    double_terminated_path_to[destination.wstring().length() + 1] = 0;

    SHFILEOPSTRUCT file_operation_params = { 0 };
    file_operation_params.wFunc = FO_COPY;
    file_operation_params.hwnd = window_handle;
    file_operation_params.pFrom = double_terminated_path_from;
    file_operation_params.pTo = double_terminated_path_to;
    file_operation_params.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOF_NOCOPYSECURITYATTRIBS;

    const int result = SHFileOperation(&file_operation_params);

    if (result != 0)
    {
        throw std::runtime_error("Failed to copy template");
    }

    return destination;
}

void template_item::refresh_target(const std::filesystem::path target_final_fullpath) const
{
    SHChangeNotify(SHCNE_CREATE, SHCNF_PATH | SHCNF_FLUSH, target_final_fullpath.wstring().c_str(), NULL);
}

void template_item::enter_rename_mode(const std::filesystem::path target_fullpath) const
{
    auto target_fullpath_copy = std::make_unique<std::filesystem::path>(target_fullpath);
    const HMODULE module_reference = acquire_background_worker_module_reference();
    try
    {
        std::thread thread_for_renaming_workaround(rename_on_other_thread_workaround, target_fullpath_copy.get(), module_reference);
        target_fullpath_copy.release();
        thread_for_renaming_workaround.detach();
    }
    catch (...)
    {
        release_background_worker_module_reference(module_reference);
        throw;
    }
}

void template_item::enter_rename_mode_and_resolve_variables(const std::filesystem::path target_fullpath) const
{
    auto target_fullpath_copy = std::make_unique<std::filesystem::path>(target_fullpath);
    const HMODULE module_reference = acquire_background_worker_module_reference();
    try
    {
        std::thread thread(rename_and_resolve_variables_on_other_thread, target_fullpath_copy.get(), module_reference);
        target_fullpath_copy.release();
        thread.detach();
    }
    catch (...)
    {
        release_background_worker_module_reference(module_reference);
        throw;
    }
}

void template_item::rename_on_other_thread_workaround(std::filesystem::path* target_fullpath, HMODULE module_reference)
{
    background_worker_lifetime_guard worker_guard(module_reference);
    std::unique_ptr<std::filesystem::path> target_fullpath_ptr(target_fullpath);

    try
    {
        // Have been unable to have Windows Explorer Shell enter rename mode from the main thread
        // Sleep for a bit to only enter rename mode when icon has been drawn.
        const std::chrono::milliseconds approx_wait_for_icon_redraw_not_needed{ 50 };
        std::this_thread::sleep_for(std::chrono::milliseconds(approx_wait_for_icon_redraw_not_needed));

        newplus::utilities::explorer_enter_rename_mode(*target_fullpath_ptr);
    }
    catch (const std::exception& ex)
    {
        Logger::error(ex.what());
    }

    target_fullpath_ptr.reset();
    worker_guard.release_and_exit_thread();
}

void template_item::rename_and_resolve_variables_on_other_thread(std::filesystem::path* target_fullpath, HMODULE module_reference)
{
    background_worker_lifetime_guard worker_guard(module_reference);
    std::unique_ptr<std::filesystem::path> target_fullpath_ptr(target_fullpath);

    try
    {
        com_initialize_guard com_guard;

        // Have been unable to have Windows Explorer Shell enter rename mode from the main thread
        // Sleep for a bit to only enter rename mode when icon has been drawn.
        const std::chrono::milliseconds icon_redraw_delay_ms{ 50 };
        std::this_thread::sleep_for(icon_redraw_delay_ms);

        const std::filesystem::path parent_dir = target_fullpath_ptr->parent_path();
        const std::wstring original_folder_name = target_fullpath_ptr->filename().wstring();
        const file_identity target_identity = get_file_identity(*target_fullpath_ptr);

        // Default to the original path in case the rename is cancelled or we cannot monitor it
        std::filesystem::path final_path = *target_fullpath_ptr;
        bool should_resolve_variables = true;
        bool should_locate_final_path_by_identity = false;
        HWND list_view_window = nullptr;

        // Track whether we have already entered rename mode so we enter it exactly once
        bool entered_rename_mode = false;

        // Open the parent directory with overlapped I/O so we can watch for the folder rename.
        // The handle must be opened before entering rename mode to avoid missing the event.
        HANDLE dir_handle = CreateFileW(
            parent_dir.c_str(),
            FILE_LIST_DIRECTORY,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OVERLAPPED,
            nullptr);

        if (dir_handle != INVALID_HANDLE_VALUE)
        {
            constexpr DWORD directory_change_buffer_size = 64 * 1024;
            std::vector<BYTE> buffer(directory_change_buffer_size);
            OVERLAPPED overlapped{};
            overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

            if (overlapped.hEvent != nullptr)
            {
                bool read_pending = ReadDirectoryChangesW(
                    dir_handle,
                    buffer.data(),
                    static_cast<DWORD>(buffer.size()),
                    FALSE, // Not recursive – we only care about direct children
                    FILE_NOTIFY_CHANGE_DIR_NAME,
                    nullptr,
                    &overlapped,
                    nullptr) != FALSE;

                if (read_pending)
                {
                    // Enter rename mode so the user can give the folder its final name.
                    list_view_window = newplus::utilities::explorer_enter_rename_mode_and_get_list_view_window(*target_fullpath_ptr);
                    entered_rename_mode = true;
                    if (list_view_window == nullptr)
                    {
                        Logger::error(L"Could not find Explorer list view window for New+ rename monitoring");
                    }

                    bool saw_rename_edit_control = false;
                    const auto monitoring_started = std::chrono::steady_clock::now();
                    bool keep_monitoring = true;
                    bool found_old_name = false;

                    const auto consume_completed_notification = [&]() -> bool {
                        DWORD bytes_returned = 0;
                        if (!GetOverlappedResult(dir_handle, &overlapped, &bytes_returned, FALSE))
                        {
                            Logger::error(L"GetOverlappedResult failed while monitoring New+ folder rename");
                            Logger::error(std::system_category().message(GetLastError()));
                            read_pending = false;
                            keep_monitoring = false;
                            should_locate_final_path_by_identity = true;
                            return false;
                        }

                        read_pending = false;

                        if (bytes_returned > 0 && process_directory_notifications_for_target_rename(buffer.data(), bytes_returned, original_folder_name, parent_dir, final_path, found_old_name))
                        {
                            keep_monitoring = false;
                            return true;
                        }

                        ResetEvent(overlapped.hEvent);
                        read_pending = ReadDirectoryChangesW(
                            dir_handle,
                            buffer.data(),
                            static_cast<DWORD>(buffer.size()),
                            FALSE,
                            FILE_NOTIFY_CHANGE_DIR_NAME,
                            nullptr,
                            &overlapped,
                            nullptr) != FALSE;

                        if (!read_pending)
                        {
                            Logger::error(L"ReadDirectoryChangesW rearm failed while monitoring New+ folder rename");
                            Logger::error(std::system_category().message(GetLastError()));
                            keep_monitoring = false;
                            should_locate_final_path_by_identity = true;
                        }

                        return false;
                    };

                    while (keep_monitoring)
                    {
                        const DWORD wait_result = WaitForSingleObject(overlapped.hEvent, rename_monitoring_poll_interval_ms);
                        if (wait_result == WAIT_OBJECT_0)
                        {
                            if (consume_completed_notification())
                            {
                                break;
                            }
                        }
                        else if (wait_result == WAIT_TIMEOUT)
                        {
                            bool should_stop_monitoring = false;
                            bool stopped_due_to_unknown_rename_state = false;

                            if (list_view_window == nullptr)
                            {
                                should_stop_monitoring = std::chrono::steady_clock::now() - monitoring_started >= rename_mode_startup_timeout;
                                stopped_due_to_unknown_rename_state = should_stop_monitoring;
                            }
                            else if (is_rename_mode_active(list_view_window))
                            {
                                saw_rename_edit_control = true;
                            }
                            else if (saw_rename_edit_control)
                            {
                                // Rename mode ended (commit/cancel) or failed to enter; either way,
                                // stop monitoring and fall back to the current final_path value.
                                should_stop_monitoring = true;
                            }
                            else if (std::chrono::steady_clock::now() - monitoring_started >= rename_mode_startup_timeout)
                            {
                                // Rename mode did not appear in time, so stop monitoring and fall
                                // back to the current final_path value.
                                should_stop_monitoring = true;
                            }

                            if (should_stop_monitoring)
                            {
                                if (read_pending && WaitForSingleObject(overlapped.hEvent, 0) == WAIT_OBJECT_0)
                                {
                                    if (consume_completed_notification())
                                    {
                                        break;
                                    }

                                    continue;
                                }

                                if (stopped_due_to_unknown_rename_state)
                                {
                                    should_resolve_variables = false;
                                }

                                keep_monitoring = false;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (read_pending)
                    {
                        CancelIoEx(dir_handle, &overlapped);
                        DWORD bytes_transferred = 0;
                        if (!GetOverlappedResult(dir_handle, &overlapped, &bytes_transferred, TRUE))
                        {
                            const DWORD error = GetLastError();
                            if (error != ERROR_OPERATION_ABORTED)
                            {
                                Logger::error(L"GetOverlappedResult failed while draining cancelled New+ folder rename monitor");
                                Logger::error(std::system_category().message(error));
                            }
                        }
                    }
                }

                CloseHandle(overlapped.hEvent);
            }

            CloseHandle(dir_handle);
        }

        if (!entered_rename_mode)
        {
            // Monitoring could not be set up; fall back to plain rename mode
            list_view_window = newplus::utilities::explorer_enter_rename_mode_and_get_list_view_window(*target_fullpath_ptr);
            entered_rename_mode = true;
            should_locate_final_path_by_identity = true;
        }

        if (should_locate_final_path_by_identity)
        {
            if (wait_for_rename_mode_to_finish(list_view_window) && try_find_child_by_identity(parent_dir, target_identity, final_path))
            {
                should_resolve_variables = true;
            }
            else
            {
                should_resolve_variables = false;
            }
        }

        if (should_resolve_variables)
        {
            // Resolve variables in the folder's contents using the final (user-provided) folder name.
            // If rename was cancelled or not observed, final_path remains target_fullpath.
            newplus::helpers::variables::resolve_variables_in_filename_and_rename_files(final_path);
        }

    }
    catch (const std::filesystem::filesystem_error& ex)
    {
        Logger::error(L"Filesystem exception while monitoring rename");
        Logger::error(target_fullpath_ptr->wstring().c_str());
        Logger::error(ex.what());
    }
    catch (const std::exception& ex)
    {
        Logger::error(L"Exception while monitoring rename");
        Logger::error(target_fullpath_ptr->wstring().c_str());
        Logger::error(ex.what());
    }
    catch (...)
    {
        Logger::error(L"Unhandled exception while monitoring rename");
        Logger::error(target_fullpath_ptr->wstring().c_str());
    }

    target_fullpath_ptr.reset();
    worker_guard.release_and_exit_thread();
}
