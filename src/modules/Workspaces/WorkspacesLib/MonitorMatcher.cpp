#include "pch.h"
#include "MonitorMatcher.h"

#include <algorithm>

namespace WorkspacesData
{
    std::vector<WorkspacesProject::Monitor>::const_iterator FindMatchingMonitor(const WorkspacesProject::Monitor& savedMonitor, const std::vector<WorkspacesProject::Monitor>& savedMonitors, const std::vector<WorkspacesProject::Monitor>& currentMonitors)
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
        const auto isKnownMonitorRect = [](const WorkspacesProject::Monitor::MonitorRect& rect) {
            return rect.top != 0 || rect.left != 0 || rect.width != 0 || rect.height != 0;
        };
        const auto hasIncompleteHardwareIdentity = [](const WorkspacesProject::Monitor& monitor) {
            return monitor.id.empty() || monitor.instanceId.empty();
        };
        const auto isCompleteIdentityOfDifferentSavedMonitor = [&](const WorkspacesProject::Monitor& currentMonitor) {
            if (hasIncompleteHardwareIdentity(currentMonitor))
            {
                return false;
            }

            return std::any_of(savedMonitors.begin(), savedMonitors.end(), [&](const WorkspacesProject::Monitor& monitor) {
                return monitor.id == currentMonitor.id &&
                       monitor.instanceId == currentMonitor.instanceId &&
                       (monitor.id != savedMonitor.id || monitor.instanceId != savedMonitor.instanceId);
            });
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
            const auto savedMonitorIdIsUnique = std::count_if(savedMonitors.begin(), savedMonitors.end(), [&](const WorkspacesProject::Monitor& monitor) {
                return monitor.id == savedMonitor.id;
            }) == 1;
            if (savedMonitorIdIsUnique)
            {
                const auto monitorByUniqueId = findUniqueMonitorMatch([&](const WorkspacesProject::Monitor& currentMonitor) {
                    return currentMonitor.id == savedMonitor.id;
                });
                if (monitorByUniqueId != end)
                {
                    return monitorByUniqueId;
                }
            }

            if (isKnownMonitorRect(savedMonitor.monitorRectDpiAware))
            {
                const auto monitorByDpiAwareRect = findUniqueMonitorMatch([&](const WorkspacesProject::Monitor& currentMonitor) {
                    return currentMonitor.id == savedMonitor.id &&
                           currentMonitor.monitorRectDpiAware == savedMonitor.monitorRectDpiAware &&
                           !isCompleteIdentityOfDifferentSavedMonitor(currentMonitor);
                });
                if (monitorByDpiAwareRect != end)
                {
                    return monitorByDpiAwareRect;
                }
            }

            if (isKnownMonitorRect(savedMonitor.monitorRectDpiUnaware))
            {
                const auto monitorByDpiUnawareRect = findUniqueMonitorMatch([&](const WorkspacesProject::Monitor& currentMonitor) {
                    return currentMonitor.id == savedMonitor.id &&
                           currentMonitor.monitorRectDpiUnaware == savedMonitor.monitorRectDpiUnaware &&
                           !isCompleteIdentityOfDifferentSavedMonitor(currentMonitor);
                });
                if (monitorByDpiUnawareRect != end)
                {
                    return monitorByDpiUnawareRect;
                }
            }
        }

        if (hasIncompleteHardwareIdentity(savedMonitor))
        {
            return std::find_if(currentMonitors.begin(), currentMonitors.end(), [&](const WorkspacesProject::Monitor& currentMonitor) {
                return currentMonitor.number == savedMonitor.number;
            });
        }

        return std::find_if(currentMonitors.begin(), currentMonitors.end(), [&](const WorkspacesProject::Monitor& currentMonitor) {
            return currentMonitor.number == savedMonitor.number && hasIncompleteHardwareIdentity(currentMonitor);
        });
    }
}
