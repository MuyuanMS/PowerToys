#pragma once

#include <common/Telemetry/EtwTrace/EtwTrace.h>

extern HMODULE module_instance_handle;
extern Shared::Trace::ETWTrace trace;

void increment_background_worker_count();
void decrement_background_worker_count();