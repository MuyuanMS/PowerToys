// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenTranslator.Keyboard;
using Windows.System;

namespace ScreenTranslator.UnitTests;

[TestClass]
public class KeyboardShortcutRouterTests
{
    private static readonly KeyboardShortcutContext ActiveUndoContext = new(
        HasVisibleResultOverlay: true,
        IsResultOverlayActive: true,
        IsEditingResultCard: false,
        HasSelectedResultCard: true,
        CanUndoSelectedResultCard: true);

    [TestMethod]
    public void ShouldHandleUndo_NoOverlay_PassesThrough()
    {
        var router = CreateRouterWithControlDown();

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), default));
    }

    [TestMethod]
    public void ShouldHandleUndo_HiddenOverlay_PassesThrough()
    {
        var router = CreateRouterWithControlDown();
        var context = ActiveUndoContext with { HasVisibleResultOverlay = false };

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), context));
    }

    [TestMethod]
    public void ShouldHandleUndo_InactiveOverlay_PassesThrough()
    {
        var router = CreateRouterWithControlDown();
        var context = ActiveUndoContext with { IsResultOverlayActive = false };

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), context));
    }

    [TestMethod]
    public void ShouldHandleUndo_NoSelectedCard_PassesThrough()
    {
        var router = CreateRouterWithControlDown();
        var context = ActiveUndoContext with { HasSelectedResultCard = false };

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), context));
    }

    [TestMethod]
    public void ShouldHandleUndo_SelectedCardWithoutUndo_PassesThrough()
    {
        var router = CreateRouterWithControlDown();
        var context = ActiveUndoContext with { CanUndoSelectedResultCard = false };

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), context));
    }

    [TestMethod]
    public void ShouldHandleUndo_ActiveCardWithUndo_HandlesKeyDown()
    {
        var router = CreateRouterWithControlDown();

        Assert.IsTrue(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), ActiveUndoContext));
    }

    [TestMethod]
    public void ShouldHandleUndo_EditingResultCard_PassesThrough()
    {
        var router = CreateRouterWithControlDown();
        var context = ActiveUndoContext with { IsEditingResultCard = true };

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), context));
    }

    [TestMethod]
    public void ShouldHandleUndo_ControlZKeyUp_PassesThrough()
    {
        var router = CreateRouterWithControlDown();

        Assert.IsFalse(router.ShouldHandleUndo(KeyUp(VirtualKey.Z), ActiveUndoContext));
    }

    [TestMethod]
    public void ShouldHandleUndo_LeftControlKeyDown_HandlesNextZ()
    {
        var state = new Dictionary<VirtualKey, bool>
        {
            [VirtualKey.LeftControl] = true,
        };
        var router = new KeyboardShortcutRouter(key => state.TryGetValue(key, out bool isDown) && isDown);

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.LeftControl), ActiveUndoContext));
        Assert.IsTrue(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), ActiveUndoContext));
    }

    [TestMethod]
    public void ShouldHandleUndo_RightControlKeyDown_HandlesNextZ()
    {
        var state = new Dictionary<VirtualKey, bool>
        {
            [VirtualKey.RightControl] = true,
        };
        var router = new KeyboardShortcutRouter(key => state.TryGetValue(key, out bool isDown) && isDown);

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.RightControl), ActiveUndoContext));
        Assert.IsTrue(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), ActiveUndoContext));
    }

    [TestMethod]
    public void ShouldHandleUndo_UnrelatedKey_PassesThrough()
    {
        var router = CreateRouterWithControlDown();

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Y), ActiveUndoContext));
    }

    [TestMethod]
    public void ShouldHandleUndo_ControlShiftZ_PassesThrough()
    {
        var state = new Dictionary<VirtualKey, bool>
        {
            [VirtualKey.LeftControl] = true,
            [VirtualKey.Shift] = true,
        };
        var router = new KeyboardShortcutRouter(key => state.TryGetValue(key, out bool isDown) && isDown);

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), ActiveUndoContext));
    }

    [TestMethod]
    public void ShouldHandleUndo_ControlReleasedBeforeZ_PassesThrough()
    {
        var state = new Dictionary<VirtualKey, bool>
        {
            [VirtualKey.LeftControl] = true,
        };
        var router = new KeyboardShortcutRouter(key => state.TryGetValue(key, out bool isDown) && isDown);

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.LeftControl), ActiveUndoContext));
        state[VirtualKey.LeftControl] = false;

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), ActiveUndoContext));
    }

    [TestMethod]
    public void ShouldHandleUndo_ExplicitControlKeyUpClearsTrackedState()
    {
        var state = new Dictionary<VirtualKey, bool>
        {
            [VirtualKey.LeftControl] = true,
        };
        var router = new KeyboardShortcutRouter(key => state.TryGetValue(key, out bool isDown) && isDown);

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.LeftControl), ActiveUndoContext));
        state[VirtualKey.LeftControl] = false;
        Assert.IsFalse(router.ShouldHandleUndo(KeyUp(VirtualKey.LeftControl), ActiveUndoContext));

        Assert.IsFalse(router.ShouldHandleUndo(KeyDown(VirtualKey.Z), ActiveUndoContext));
    }

    private static KeyboardShortcutRouter CreateRouterWithControlDown()
    {
        return new KeyboardShortcutRouter(key => key is VirtualKey.Control or VirtualKey.LeftControl);
    }

    private static GlobalKeyboardHookEventArgs KeyDown(VirtualKey key)
    {
        return new GlobalKeyboardHookEventArgs(key, isKeyDown: true);
    }

    private static GlobalKeyboardHookEventArgs KeyUp(VirtualKey key)
    {
        return new GlobalKeyboardHookEventArgs(key, isKeyDown: false);
    }
}
