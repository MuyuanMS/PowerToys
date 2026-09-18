#pragma once
void init_global_error_handlers();

// Write a null-terminated ASCII message to the persistent crash-marker file.
// Allocation-free and mutex-free; safe to call from std::set_terminate handlers
// and other contexts where Logger may not be initialised or safe to invoke.
void write_crash_marker_message(const char* msg);
