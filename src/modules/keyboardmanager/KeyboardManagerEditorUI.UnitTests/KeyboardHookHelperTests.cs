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
        public void ShouldMoveFocusBackward_ReturnsTrue_WhenShiftWasPressedBeforeHookActivation()
        {
            Assert.IsTrue(KeyboardHookHelper.ShouldMoveFocusBackward(
                new HashSet<VirtualKey>(),
                shiftIsPressed: true));
        }

        [TestMethod]
        public void ShouldMoveFocusBackward_ReturnsTrue_WhenTrackedShiftIsPressed()
        {
            Assert.IsTrue(KeyboardHookHelper.ShouldMoveFocusBackward(
                new HashSet<VirtualKey> { VirtualKey.LeftShift },
                shiftIsPressed: false));
        }

        [TestMethod]
        public void ShouldMoveFocusBackward_ReturnsFalse_WhenShiftIsNotPressed()
        {
            Assert.IsFalse(KeyboardHookHelper.ShouldMoveFocusBackward(
                new HashSet<VirtualKey>(),
                shiftIsPressed: false));
        }
    }
}
