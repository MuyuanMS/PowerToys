#pragma once

#include <WorkspacesLib/WorkspacesData.h>

namespace WorkspacesData
{
    std::vector<WorkspacesProject::Monitor>::const_iterator FindMatchingMonitor(const WorkspacesProject::Monitor& savedMonitor, const std::vector<WorkspacesProject::Monitor>& savedMonitors, const std::vector<WorkspacesProject::Monitor>& currentMonitors);
}
