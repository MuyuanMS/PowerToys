#include "pch.h"
#include "MonitorMatcher.h"

#include <algorithm>

namespace WorkspacesData
{
    std::vector<WorkspacesProject::Monitor>::const_iterator FindMatchingMonitor(const WorkspacesProject::Monitor& savedMonitor, const std::vector<WorkspacesProject::Monitor>& currentMonitors)
    {
        const auto end = currentMonitors.end();
        const auto findUniqueMonitorMatch = [&](const auto& predicate) {
            const auto firstMatch = std::find_if(currentMonitors.begin(), currentMonitors.end(), predicate);
            if (firstMatch == end)
            {
                return firstMatch;
            }

            return std::find_if(firstMatch + 1, end, predicate) == end ? firstMatch : end;
        };

        if (!savedMonitor.id.empty() && !savedMonitor.instanceId.empty())
        {
            const auto monitor = std::find_if(currentMonitors.begin(), currentMonitors.end(), [&](const WorkspacesProject::Monitor& currentMonitor) {
                return currentMonitor.id == savedMonitor.id && currentMonitor.instanceId == savedMonitor.instanceId;
            });
            if (monitor != end)
            {
                return monitor;
            }
        }

        if (!savedMonitor.id.empty())
        {
            const auto monitorByUniqueId = findUniqueMonitorMatch([&](const WorkspacesProject::Monitor& currentMonitor) {
                return currentMonitor.id == savedMonitor.id;
            });
            if (monitorByUniqueId != end)
            {
                return monitorByUniqueId;
            }

            const auto monitorByDpiAwareRect = findUniqueMonitorMatch([&](const WorkspacesProject::Monitor& currentMonitor) {
                return currentMonitor.id == savedMonitor.id && currentMonitor.monitorRectDpiAware == savedMonitor.monitorRectDpiAware;
            });
            if (monitorByDpiAwareRect != end)
            {
                return monitorByDpiAwareRect;
            }

            const auto monitorByDpiUnawareRect = findUniqueMonitorMatch([&](const WorkspacesProject::Monitor& currentMonitor) {
                return currentMonitor.id == savedMonitor.id && currentMonitor.monitorRectDpiUnaware == savedMonitor.monitorRectDpiUnaware;
            });
            if (monitorByDpiUnawareRect != end)
            {
                return monitorByDpiUnawareRect;
            }
        }

        if (savedMonitor.id.empty() || savedMonitor.instanceId.empty())
        {
            return std::find_if(currentMonitors.begin(), currentMonitors.end(), [&](const WorkspacesProject::Monitor& currentMonitor) {
                return currentMonitor.number == savedMonitor.number && (currentMonitor.id.empty() || savedMonitor.id.empty()) && (currentMonitor.instanceId.empty() || savedMonitor.instanceId.empty());
            });
        }

        return end;
    }
}
