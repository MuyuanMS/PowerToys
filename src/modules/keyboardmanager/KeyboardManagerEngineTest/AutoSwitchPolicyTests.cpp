#include "pch.h"

#pragma warning(push)
#pragma warning(disable : 26466)
#include "CppUnitTest.h"
#pragma warning(pop)
#include "..\KeyboardManagerEngineLibrary\AutoSwitchPolicy.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace AutoSwitchPolicyTests
{
TEST_CLASS(AutoSwitchPolicyTests)
{
public:
    TEST_METHOD(IgnoresInjectedKeyUpsAndModifiers)
    {
        Assert::IsTrue(KeyboardManagerAutoSwitchPolicy::ShouldIgnoreEvent(true, true, 'A'));
        Assert::IsTrue(KeyboardManagerAutoSwitchPolicy::ShouldIgnoreEvent(false, false, 'A'));
        Assert::IsTrue(KeyboardManagerAutoSwitchPolicy::ShouldIgnoreEvent(false, true, VK_LSHIFT));
        Assert::IsFalse(KeyboardManagerAutoSwitchPolicy::ShouldIgnoreEvent(false, true, 'A'));
    }

    TEST_METHOD(ResetsAndTriggersHysteresisPerTarget)
    {
        KeyboardManagerAutoSwitchPolicy::State state;
        Assert::IsFalse(KeyboardManagerAutoSwitchPolicy::AdvanceHysteresis(state, L"mac", 2));
        Assert::AreEqual(1, state.pendingCount);
        Assert::IsTrue(KeyboardManagerAutoSwitchPolicy::AdvanceHysteresis(state, L"mac", 2));
        Assert::AreEqual(0, state.pendingCount);
        Assert::IsFalse(KeyboardManagerAutoSwitchPolicy::AdvanceHysteresis(state, L"surface", 2));
        Assert::AreEqual(1, state.pendingCount);
    }

    TEST_METHOD(WaitsOnlyForAStillInactiveRequestedProfile)
    {
        KeyboardManagerAutoSwitchPolicy::State state;
        state.requestedProfile = L"mac";
        Assert::IsTrue(KeyboardManagerAutoSwitchPolicy::IsAwaitingRequestedProfile(state, L"mac", L"default"));
        Assert::IsFalse(KeyboardManagerAutoSwitchPolicy::IsAwaitingRequestedProfile(state, L"mac", L"mac"));
        Assert::IsFalse(KeyboardManagerAutoSwitchPolicy::IsAwaitingRequestedProfile(state, L"surface", L"default"));
    }
};
}
