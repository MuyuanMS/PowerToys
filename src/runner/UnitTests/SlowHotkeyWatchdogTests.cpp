// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include "pch.h"
#include "CppUnitTest.h"

#include "../slow_hotkey_watchdog.h"
#include "../centralized_hotkeys.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::chrono_literals;

namespace RunnerUnitTests
{
    TEST_CLASS (SlowHotkeyWatchdogTests)
    {
    public:
        TEST_METHOD (OnlyDurationsAboveThresholdAreSlow)
        {
            Assert::IsFalse(SlowHotkeyWatchdog::IsSlow(0ms));
            Assert::IsFalse(SlowHotkeyWatchdog::IsSlow(50ms));
            Assert::IsTrue(SlowHotkeyWatchdog::IsSlow(50ms + 1us));
            Assert::IsTrue(SlowHotkeyWatchdog::IsSlow(800ms));
        }

        TEST_METHOD (EachModuleHotkeyPairIsReportedOnce)
        {
            SlowHotkeyWatchdog::ReportLimiter limiter;
            Assert::IsTrue(limiter.TryMarkReported(L"AdvancedPaste", L"win+shift+VK 0x56"));
            Assert::IsFalse(limiter.TryMarkReported(L"AdvancedPaste", L"win+shift+VK 0x56"));

            // A different hotkey of the same module, or the same hotkey of another module, is reported separately.
            Assert::IsTrue(limiter.TryMarkReported(L"AdvancedPaste", L"ctrl+win+alt+VK 0x56"));
            Assert::IsTrue(limiter.TryMarkReported(L"LightSwitch", L"win+shift+VK 0x56"));
            Assert::IsFalse(limiter.TryMarkReported(L"LightSwitch", L"win+shift+VK 0x56"));
        }

        TEST_METHOD (DescribeHotkeyIsLayoutIndependent)
        {
            Assert::AreEqual(std::wstring(L"win+ctrl+shift+alt+VK 0x56"), SlowHotkeyWatchdog::DescribeHotkey(true, true, true, true, 0x56));
            Assert::AreEqual(std::wstring(L"VK 0x5B"), SlowHotkeyWatchdog::DescribeHotkey(false, false, false, false, VK_LWIN));
        }

        TEST_METHOD (WMHotkeyIdentityUsesVirtualKeyAndIgnoresNoRepeat)
        {
            const CentralizedHotkeys::Shortcut shortcut{ MOD_SHIFT | MOD_ALT, VK_OEM_1 };
            const CentralizedHotkeys::Shortcut repeated{ MOD_SHIFT | MOD_ALT | MOD_NOREPEAT, VK_OEM_1 };
            Assert::AreEqual(std::wstring(L"shift+alt+VK 0xBA"), CentralizedHotkeys::ToWatchdogHotkey(shortcut));
            Assert::AreEqual(CentralizedHotkeys::ToWatchdogHotkey(shortcut), CentralizedHotkeys::ToWatchdogHotkey(repeated));
        }

        TEST_METHOD (WMHotkeyAndKeyboardHookShareLimiterIdentity)
        {
            const CentralizedHotkeys::Shortcut shortcut{ MOD_WIN | MOD_CONTROL | MOD_SHIFT | MOD_ALT, 0x56 };
            SlowHotkeyWatchdog::ReportLimiter limiter;
            Assert::IsTrue(limiter.TryMarkReported(L"TestModule", SlowHotkeyWatchdog::DescribeHotkey(true, true, true, true, 0x56)));
            Assert::IsFalse(limiter.TryMarkReported(L"TestModule", CentralizedHotkeys::ToWatchdogHotkey(shortcut)));
        }
    };
}
