// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.CmdPal.Ext.Apps.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.Apps.UnitTests;

[TestClass]
public class RunCommandTests
{
    [TestMethod]
    public async Task RunAsAdmin_PackagedLaunchFailure_DoesNotThrow()
    {
        await RunAsAdminCommand.RunAsAdmin("TestPackage_123!App", string.Empty, true, ThrowOnStart);
    }

    [TestMethod]
    public async Task RunAsAdmin_Win32LaunchFailure_DoesNotThrow()
    {
        await RunAsAdminCommand.RunAsAdmin(@"C:\Windows\System32\notepad.exe", @"C:\Windows\System32", false, ThrowOnStart);
    }

    [TestMethod]
    public async Task RunAsUser_LaunchFailure_DoesNotThrow()
    {
        await RunAsUserCommand.RunAsUser(@"C:\Windows\System32\notepad.exe", @"C:\Windows\System32", ThrowOnStart);
    }

    private static Process ThrowOnStart(ProcessStartInfo processStartInfo)
    {
        throw new InvalidOperationException("Simulated launch failure");
    }
}
