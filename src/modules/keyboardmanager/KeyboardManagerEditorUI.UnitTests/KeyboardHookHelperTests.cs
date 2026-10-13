// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using KeyboardManagerEditorUI.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.System;

namespace KeyboardManagerEditorUI.UnitTests
{
    [TestClass]
    public class KeyboardHookHelperTests
    {
        [TestMethod]
        public void Tab_ExitsRecordingAndMovesFocusForward_WhenNoKeysArePressed()
        {
            var decision = KeyboardHookHelper.GetRecordingExitDecision(
                VirtualKey.Tab,
                new HashSet<VirtualKey>(),
                hasCapturedKeys: false,
                hasCapturedNonShiftKeys: false,
                shiftIsPressed: false);

            Assert.IsTrue(decision.ShouldExitRecording);
            Assert.IsFalse(decision.MoveFocusBackward);
            Assert.IsTrue(decision.RestorePreviousKeys);
        }

        [TestMethod]
        public void ShiftTab_ExitsRecordingAndMovesFocusBackward_WhenShiftIsTracked()
        {
            var decision = KeyboardHookHelper.GetRecordingExitDecision(
                VirtualKey.Tab,
                new HashSet<VirtualKey> { VirtualKey.LeftShift },
                hasCapturedKeys: true,
                hasCapturedNonShiftKeys: false,
                shiftIsPressed: false);

            Assert.IsTrue(decision.ShouldExitRecording);
            Assert.IsTrue(decision.MoveFocusBackward);
            Assert.IsTrue(decision.RestorePreviousKeys);
        }

        [TestMethod]
        public void ShiftTab_ExitsRecordingAndMovesFocusBackward_WhenShiftWasPressedBeforeHookActivation()
        {
            var decision = KeyboardHookHelper.GetRecordingExitDecision(
                VirtualKey.Tab,
                new HashSet<VirtualKey>(),
                hasCapturedKeys: false,
                hasCapturedNonShiftKeys: false,
                shiftIsPressed: true);

            Assert.IsTrue(decision.ShouldExitRecording);
            Assert.IsTrue(decision.MoveFocusBackward);
            Assert.IsTrue(decision.RestorePreviousKeys);
        }

        [TestMethod]
        public void Tab_RetainsCapturedKeys_WhenRecordingHasAlreadyCapturedAndReleasedAKey()
        {
            var decision = KeyboardHookHelper.GetRecordingExitDecision(
                VirtualKey.Tab,
                new HashSet<VirtualKey>(),
                hasCapturedKeys: true,
                hasCapturedNonShiftKeys: true,
                shiftIsPressed: false);

            Assert.IsTrue(decision.ShouldExitRecording);
            Assert.IsFalse(decision.MoveFocusBackward);
            Assert.IsFalse(decision.RestorePreviousKeys);
        }

        [TestMethod]
        public void ShiftTab_RetainsPreviouslyCapturedNonShiftKeys()
        {
            var decision = KeyboardHookHelper.GetRecordingExitDecision(
                VirtualKey.Tab,
                new HashSet<VirtualKey> { VirtualKey.LeftShift },
                hasCapturedKeys: true,
                hasCapturedNonShiftKeys: true,
                shiftIsPressed: false);

            Assert.IsTrue(decision.ShouldExitRecording);
            Assert.IsTrue(decision.MoveFocusBackward);
            Assert.IsFalse(decision.RestorePreviousKeys);
        }

        [TestMethod]
        public void ControlTab_DoesNotExitRecording()
        {
            var decision = KeyboardHookHelper.GetRecordingExitDecision(
                VirtualKey.Tab,
                new HashSet<VirtualKey> { VirtualKey.LeftControl },
                hasCapturedKeys: true,
                hasCapturedNonShiftKeys: true,
                shiftIsPressed: false);

            Assert.IsFalse(decision.ShouldExitRecording);
            Assert.IsFalse(decision.MoveFocusBackward);
            Assert.IsFalse(decision.RestorePreviousKeys);
        }
    }
}
