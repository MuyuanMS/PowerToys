// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkspacesLauncherUI.Data;
using WorkspacesLauncherUI.Models;

namespace WorkspacesLauncherUI.UnitTests
{
    /// <summary>
    /// Tests for the AppLaunching model which drives UI display:
    /// loading indicator, state glyph, and state color.
    /// </summary>
    [TestClass]
    public class LaunchStatusDisplayLogicTests
    {
        [TestMethod]
        [TestCategory("Model")]
        public void LoadingSpinner_WhenStateIsWaiting_IsVisible()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.Waiting };
            Assert.IsTrue(app.Loading);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void LoadingSpinner_WhenStateIsLaunched_RemainsVisibleUntilMoved()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.Launched };
            Assert.IsTrue(app.Loading);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void LoadingSpinner_WhenStateIsLaunchedAndMoved_IsHidden()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.LaunchedAndMoved };
            Assert.IsFalse(app.Loading);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void LoadingSpinner_WhenStateIsFailed_IsHidden()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.Failed };
            Assert.IsFalse(app.Loading);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void LoadingSpinner_WhenStateIsCanceled_IsHidden()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.Canceled };
            Assert.IsFalse(app.Loading);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void StatusIcon_WhenSuccessful_ExposesLaunchedAndMovedState()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.LaunchedAndMoved };
            Assert.AreEqual((int)LaunchingState.LaunchedAndMoved, app.LaunchStateInt);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void StatusIcon_WhenFailed_ExposesFailedState()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.Failed };
            Assert.AreEqual((int)LaunchingState.Failed, app.LaunchStateInt);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void StatusIcon_WhenCanceled_ExposesCanceledState()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.Canceled };
            Assert.AreEqual((int)LaunchingState.Canceled, app.LaunchStateInt);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void AutomationName_WhenSuccessful_AnnouncesSuccessfulState()
        {
            var app = new AppLaunching { Name = "Test Application", LaunchState = LaunchingState.LaunchedAndMoved };
            Assert.AreEqual("Test Application: launch succeeded", app.StateAutomationName);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void AutomationName_WhenFailed_AnnouncesFailedState()
        {
            var app = new AppLaunching { Name = "Test Application", LaunchState = LaunchingState.Failed };
            Assert.AreEqual("Test Application: launch failed", app.StateAutomationName);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void AutomationName_WhenCanceled_AnnouncesCanceledState()
        {
            var app = new AppLaunching { Name = "Test Application", LaunchState = LaunchingState.Canceled };
            Assert.AreEqual("Test Application: launch canceled", app.StateAutomationName);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void AutomationName_WhenWaiting_AnnouncesApplicationAndProgress()
        {
            var app = new AppLaunching { Name = "Test Application", LaunchState = LaunchingState.Waiting };
            Assert.AreEqual("Test Application: launching", app.StateAutomationName);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void LaunchStateInt_WhenSuccessful_ReturnsExpectedValue()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.LaunchedAndMoved };
            Assert.AreEqual(2, app.LaunchStateInt);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void LaunchStateInt_WhenFailed_ReturnsExpectedValue()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.Failed };
            Assert.AreEqual(3, app.LaunchStateInt);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void LaunchStateInt_WhenCanceled_ReturnsExpectedValue()
        {
            var app = new AppLaunching { LaunchState = LaunchingState.Canceled };
            Assert.AreEqual(4, app.LaunchStateInt);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void AppName_SetToString_ReturnsExactValue()
        {
            var app = new AppLaunching { Name = "Test Application" };
            Assert.AreEqual("Test Application", app.Name);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void AppName_SetToEmpty_ReturnsEmptyString()
        {
            var app = new AppLaunching { Name = string.Empty };
            Assert.AreEqual(string.Empty, app.Name);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void StateProgression_WaitingToSuccess_TransitionsSpinnerToComplete()
        {
            var app = new AppLaunching { Name = "Test", LaunchState = LaunchingState.Waiting };
            Assert.IsTrue(app.Loading);

            app.LaunchState = LaunchingState.Launched;
            Assert.IsTrue(app.Loading);

            app.LaunchState = LaunchingState.LaunchedAndMoved;
            Assert.IsFalse(app.Loading);
            Assert.AreEqual((int)LaunchingState.LaunchedAndMoved, app.LaunchStateInt);
        }

        [TestMethod]
        [TestCategory("Model")]
        public void StateProgression_WaitingToFailed_TransitionsSpinnerToError()
        {
            var app = new AppLaunching { Name = "Test", LaunchState = LaunchingState.Waiting };
            Assert.IsTrue(app.Loading);

            app.LaunchState = LaunchingState.Failed;
            Assert.IsFalse(app.Loading);
            Assert.AreEqual((int)LaunchingState.Failed, app.LaunchStateInt);
        }
    }
}
