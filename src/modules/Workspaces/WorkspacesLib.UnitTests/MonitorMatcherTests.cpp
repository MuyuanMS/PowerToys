#include "pch.h"
#include <WorkspacesLib/MonitorMatcher.h>

#include <iterator>
#include <utility>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace WorkspacesLibUnitTests
{
    TEST_CLASS(MonitorMatcherTests)
    {
    private:
        static WorkspacesData::WorkspacesProject::Monitor Monitor(unsigned int number, std::wstring id = {}, std::wstring instanceId = {}, WorkspacesData::WorkspacesProject::Monitor::MonitorRect dpiAware = {}, WorkspacesData::WorkspacesProject::Monitor::MonitorRect dpiUnaware = {})
        {
            WorkspacesData::WorkspacesProject::Monitor monitor;
            monitor.number = number;
            monitor.id = std::move(id);
            monitor.instanceId = std::move(instanceId);
            monitor.monitorRectDpiAware = dpiAware;
            monitor.monitorRectDpiUnaware = dpiUnaware;
            return monitor;
        }

        static int MatchIndex(const WorkspacesData::WorkspacesProject::Monitor& savedMonitor, const std::vector<WorkspacesData::WorkspacesProject::Monitor>& currentMonitors)
        {
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> savedMonitors{ savedMonitor };
            const auto match = WorkspacesData::FindMatchingMonitor(savedMonitor, savedMonitors, currentMonitors);
            return match == currentMonitors.end() ? -1 : static_cast<int>(std::distance(currentMonitors.begin(), match));
        }

        static int MatchIndex(const WorkspacesData::WorkspacesProject::Monitor& savedMonitor, const std::vector<WorkspacesData::WorkspacesProject::Monitor>& savedMonitors, const std::vector<WorkspacesData::WorkspacesProject::Monitor>& currentMonitors)
        {
            const auto match = WorkspacesData::FindMatchingMonitor(savedMonitor, savedMonitors, currentMonitors);
            return match == currentMonitors.end() ? -1 : static_cast<int>(std::distance(currentMonitors.begin(), match));
        }

    public:
        TEST_METHOD(FindMatchingMonitor_UsesHardwareIdsWhenMonitorNumberChanged)
        {
            const auto saved = Monitor(1, L"MONITOR-A", L"DISPLAY\\A\\1");
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> currentMonitors{
                Monitor(1, L"MONITOR-B", L"DISPLAY\\B\\1"),
                Monitor(3, L"MONITOR-A", L"DISPLAY\\A\\1"),
            };

            Assert::AreEqual(1, MatchIndex(saved, currentMonitors));
        }

        TEST_METHOD(FindMatchingMonitor_AllowsUniqueIdWhenInstanceIdChanged)
        {
            const auto saved = Monitor(1, L"MONITOR-A", L"DISPLAY\\A\\1");
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> currentMonitors{
                Monitor(2, L"MONITOR-B", L"DISPLAY\\B\\1"),
                Monitor(3, L"MONITOR-A", L"DISPLAY\\A\\2"),
            };

            Assert::AreEqual(1, MatchIndex(saved, currentMonitors));
        }

        TEST_METHOD(FindMatchingMonitor_UsesBoundsToDisambiguateDuplicateModelIds)
        {
            const WorkspacesData::WorkspacesProject::Monitor::MonitorRect savedBounds{ 0, 1920, 1920, 1080 };
            const auto saved = Monitor(2, L"MONITOR-A", L"DISPLAY\\A\\1", savedBounds);
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> currentMonitors{
                Monitor(1, L"MONITOR-A", L"DISPLAY\\A\\2", { 0, 0, 1920, 1080 }),
                Monitor(3, L"MONITOR-A", L"DISPLAY\\A\\3", savedBounds),
            };

            Assert::AreEqual(1, MatchIndex(saved, currentMonitors));
        }

        TEST_METHOD(FindMatchingMonitor_DoesNotUseNumberWhenCompleteHardwareIdentityIsMissing)
        {
            const auto saved = Monitor(2, L"MONITOR-A", L"DISPLAY\\A\\1");
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> currentMonitors{
                Monitor(2, L"MONITOR-B", L"DISPLAY\\B\\1"),
            };

            Assert::AreEqual(-1, MatchIndex(saved, currentMonitors));
        }

        TEST_METHOD(FindMatchingMonitor_UsesNumberWhenCurrentHardwareIdentityIsIncomplete)
        {
            const auto saved = Monitor(2, L"MONITOR-A", L"DISPLAY\\A\\1");
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> currentMonitors{
                Monitor(2, L"\\\\.\\DISPLAY2"),
            };

            Assert::AreEqual(0, MatchIndex(saved, currentMonitors));
        }

        TEST_METHOD(FindMatchingMonitor_UsesNumberForLegacyNumberOnlyData)
        {
            const auto saved = Monitor(2);
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> currentMonitors{
                Monitor(1),
                Monitor(2),
            };

            Assert::AreEqual(1, MatchIndex(saved, currentMonitors));
        }

        TEST_METHOD(FindMatchingMonitor_DoesNotUseUniqueCurrentIdWhenSavedIdIsDuplicated)
        {
            const WorkspacesData::WorkspacesProject::Monitor::MonitorRect savedBounds{ 0, 1920, 1920, 1080 };
            const auto missingSavedMonitor = Monitor(2, L"MONITOR-A", L"DISPLAY\\A\\1", savedBounds);
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> savedMonitors{
                Monitor(1, L"MONITOR-A", L"DISPLAY\\A\\2", { 0, 0, 1920, 1080 }),
                missingSavedMonitor,
            };
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> currentMonitors{
                Monitor(1, L"MONITOR-A", L"DISPLAY\\A\\2", { 0, 0, 1920, 1080 }),
            };

            Assert::AreEqual(-1, MatchIndex(missingSavedMonitor, savedMonitors, currentMonitors));
        }

        TEST_METHOD(FindMatchingMonitor_DoesNotUseReflowedBoundsFromDifferentSavedMonitor)
        {
            const WorkspacesData::WorkspacesProject::Monitor::MonitorRect leftBounds{ 0, 0, 1920, 1080 };
            const WorkspacesData::WorkspacesProject::Monitor::MonitorRect rightBounds{ 0, 1920, 1920, 1080 };
            const auto disconnectedSavedMonitor = Monitor(1, L"MONITOR-A", L"DISPLAY\\A\\1", leftBounds);
            const auto reflowedSavedMonitor = Monitor(2, L"MONITOR-A", L"DISPLAY\\A\\2", rightBounds);
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> savedMonitors{
                disconnectedSavedMonitor,
                reflowedSavedMonitor,
            };
            const std::vector<WorkspacesData::WorkspacesProject::Monitor> currentMonitors{
                Monitor(1, L"MONITOR-A", L"DISPLAY\\A\\2", leftBounds),
            };

            Assert::AreEqual(-1, MatchIndex(disconnectedSavedMonitor, savedMonitors, currentMonitors));
        }
    };
}
