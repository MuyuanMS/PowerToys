// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.PowerToys.Settings.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SettingsXAML
{
    [TestClass]
    public sealed class DeferredWindowActivationTests
    {
        [TestMethod]
        public void RequestActivation_ActivatesImmediately_WhenWindowIsAlreadyVisible()
        {
            var state = new ActivationState();
            var activation = state.CreateActivation();

            activation.RequestActivation(canActivateImmediately: true, isInitialContentLoaded: false, bringToForeground: true);

            Assert.AreEqual(1, state.ActivateCount);
            Assert.AreEqual(0, state.StartTimerCount);
            Assert.IsFalse(activation.ActivationPending);
            Assert.IsTrue(activation.ConsumeBringToForeground());
        }

        [TestMethod]
        public void RequestActivation_WaitsForInitialContentLoaded_WhenWindowIsHidden()
        {
            var state = new ActivationState();
            var activation = state.CreateActivation();

            activation.RequestActivation(canActivateImmediately: false, isInitialContentLoaded: false, bringToForeground: false);

            Assert.IsTrue(activation.ActivationPending);
            Assert.AreEqual(1, state.SubscribeCount);
            Assert.AreEqual(1, state.StartTimerCount);

            activation.OnInitialContentLoaded();

            Assert.AreEqual(1, state.ActivateCount);
            Assert.AreEqual(2, state.UnsubscribeCount);
            Assert.AreEqual(1, state.StopTimerCount);
            Assert.IsFalse(activation.ActivationPending);
        }

        [TestMethod]
        public void RequestActivation_UsesFallbackTimer_WhenInitialContentDoesNotLoad()
        {
            var state = new ActivationState();
            var activation = state.CreateActivation();

            activation.RequestActivation(canActivateImmediately: false, isInitialContentLoaded: false, bringToForeground: false);
            activation.OnFallbackTimer();

            Assert.AreEqual(1, state.ActivateCount);
            Assert.AreEqual(1, state.StopTimerCount);
            Assert.IsFalse(activation.ActivationPending);
        }

        [TestMethod]
        public void RequestActivation_IgnoresStaleAsyncCompletion_AfterFallbackWins()
        {
            var state = new ActivationState();
            var activation = state.CreateActivation();

            activation.RequestActivation(canActivateImmediately: false, isInitialContentLoaded: false, bringToForeground: false);
            activation.OnFallbackTimer();
            activation.OnInitialContentLoaded();

            Assert.AreEqual(1, state.ActivateCount);
            Assert.IsFalse(activation.ActivationPending);
        }

        [TestMethod]
        public void RequestActivation_IgnoresStaleAsyncCompletion_AfterContentLoadedWins()
        {
            var state = new ActivationState();
            var activation = state.CreateActivation();

            activation.RequestActivation(canActivateImmediately: false, isInitialContentLoaded: false, bringToForeground: false);
            activation.OnInitialContentLoaded();
            activation.OnFallbackTimer();

            Assert.AreEqual(1, state.ActivateCount);
            Assert.IsFalse(activation.ActivationPending);
        }

        [TestMethod]
        public void CloseHiddenWindow_DoesNotCloseWhileActivationIsPending()
        {
            var state = new ActivationState();
            var activation = state.CreateActivation();

            activation.RequestActivation(canActivateImmediately: false, isInitialContentLoaded: false, bringToForeground: false);
            activation.CloseHiddenWindow(isWindowVisible: false);

            Assert.AreEqual(0, state.CloseCount);
            Assert.IsTrue(activation.ActivationPending);
        }

        [TestMethod]
        public void CloseHiddenWindow_ClosesHiddenWindow_WhenActivationIsNotPending()
        {
            var state = new ActivationState();
            var activation = state.CreateActivation();

            activation.CloseHiddenWindow(isWindowVisible: false);

            Assert.AreEqual(1, state.CloseCount);
        }

        [TestMethod]
        public void WindowClosed_CancelsPendingActivation()
        {
            var state = new ActivationState();
            var activation = state.CreateActivation();

            activation.RequestActivation(canActivateImmediately: false, isInitialContentLoaded: false, bringToForeground: false);
            activation.OnWindowClosed();
            activation.OnFallbackTimer();

            Assert.AreEqual(0, state.ActivateCount);
            Assert.AreEqual(1, state.StopTimerCount);
            Assert.IsFalse(activation.ActivationPending);
        }

        private sealed class ActivationState
        {
            public int ActivateCount { get; private set; }

            public int StartTimerCount { get; private set; }

            public int StopTimerCount { get; private set; }

            public int SubscribeCount { get; private set; }

            public int UnsubscribeCount { get; private set; }

            public int CloseCount { get; private set; }

            public DeferredWindowActivation CreateActivation()
            {
                return new DeferredWindowActivation(
                    () => ActivateCount++,
                    () => StartTimerCount++,
                    () => StopTimerCount++,
                    () => SubscribeCount++,
                    () => UnsubscribeCount++,
                    () => CloseCount++);
            }
        }
    }
}
