#pragma warning(push)
#pragma warning(disable : 26466)
#include "CppUnitTest.h"
#pragma warning(pop)

#include "LightSwitchBrightnessLogic.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace LightSwitchServiceUnitTests
{
    TEST_CLASS(LightSwitchBrightnessLogicTests)
    {
    public:
        TEST_METHOD(BelowThresholdIsDark)
        {
            Assert::IsFalse(LightSwitchBrightnessLogic::ShouldBeLight(49, 50));
        }

        TEST_METHOD(EqualToThresholdIsLight)
        {
            Assert::IsTrue(LightSwitchBrightnessLogic::ShouldBeLight(50, 50));
        }

        TEST_METHOD(AboveThresholdIsLight)
        {
            Assert::IsTrue(LightSwitchBrightnessLogic::ShouldBeLight(51, 50));
        }

        TEST_METHOD(UnknownBrightnessIsNotKnown)
        {
            Assert::IsFalse(LightSwitchBrightnessLogic::IsKnown(-1));
        }

        TEST_METHOD(SameSideUpdateDoesNotCrossThreshold)
        {
            Assert::IsFalse(LightSwitchBrightnessLogic::CrossedThreshold(40, 49, 50));
            Assert::IsFalse(LightSwitchBrightnessLogic::CrossedThreshold(60, 75, 50));
        }

        TEST_METHOD(CrossingThresholdIsDetected)
        {
            Assert::IsTrue(LightSwitchBrightnessLogic::CrossedThreshold(49, 50, 50));
            Assert::IsTrue(LightSwitchBrightnessLogic::CrossedThreshold(50, 49, 50));
        }

        TEST_METHOD(UnknownPreviousSampleDoesNotCrossThreshold)
        {
            Assert::IsFalse(LightSwitchBrightnessLogic::CrossedThreshold(-1, 75, 50));
        }
    };
}
