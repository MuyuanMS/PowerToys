#pragma once

#include <common/Telemetry/EtwTrace/EtwTrace.h>

extern HMODULE module_instance_handle;
extern Shared::Trace::ETWTrace trace;

HMODULE acquire_background_worker_module_reference();
void release_background_worker_module_reference(HMODULE module_reference);
[[noreturn]] void release_background_worker_module_reference_and_exit_thread(HMODULE module_reference);
