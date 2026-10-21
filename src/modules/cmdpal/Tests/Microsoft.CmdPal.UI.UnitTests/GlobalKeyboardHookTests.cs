// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.UI.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public partial class GlobalKeyboardHookTests
{
    [TestMethod]
    public void MessageLoopExitsWhenItReceivesQuitMessage()
    {
        using var ready = new ManualResetEventSlim();
        using var stop = new ManualResetEvent(false);
        var hook = new GlobalKeyboardHook();
        uint threadId = 0;
        Exception? threadError = null;
        var thread = new Thread(() =>
        {
            try
            {
                threadId = PInvoke.GetCurrentThreadId();
                _ = PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
                ready.Set();
                hook.RunMessageLoop(stop, () => { });
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
        try
        {
            Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(PInvoke.PostThreadMessage(threadId, PInvoke.WM_QUIT, default, default));
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "The message loop did not exit after WM_QUIT.");
            Assert.IsNull(threadError);
        }
        finally
        {
            stop.Set();
            _ = thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
