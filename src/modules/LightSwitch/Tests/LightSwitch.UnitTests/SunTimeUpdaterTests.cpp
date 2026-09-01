#include <CppUnitTest.h>

#include <LightSwitchService/SunTimeUpdater.h>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace LightSwitchUnitTests
{
    TEST_CLASS (SunTimeUpdaterTests)
    {
        TEST_METHOD (MalformedCoordinatesReturnNoResultWithoutSaving)
        {
            bool calculatorCalled = false;
            bool saverCalled = false;

            const auto result = LightSwitch::TryUpdateSunTimes(
                L"not-a-latitude",
                L"122.3328",
                [&](double, double, int, int, int) {
                    calculatorCalled = true;
                    return SunTimes{ 6, 15, 18, 45 };
                },
                [&](int, int) {
                    saverCalled = true;
                });

            Assert::IsFalse(result.has_value());
            Assert::IsFalse(calculatorCalled);
            Assert::IsFalse(saverCalled);
        }

        TEST_METHOD (PersistenceFailuresStillReturnCalculatedSunTimes)
        {
            const auto result = LightSwitch::TryUpdateSunTimes(
                L"47.6062",
                L"-122.3321",
                [](double, double, int, int, int) {
                    return SunTimes{ 6, 30, 18, 45 };
                },
                [](int, int) {
                    throw 42;
                });

            Assert::IsTrue(result.has_value());
            Assert::AreEqual(390, result->first);
            Assert::AreEqual(1125, result->second);
        }
    };
}
