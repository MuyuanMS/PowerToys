// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;
using Newtonsoft.Json;

namespace MouseWithoutBorders.UnitTests;

[TestClass]
public sealed class IpcSerializationCompatibilityTests
{
    [TestMethod]
    public void SettingsSyncPayloadKeepsExistingJsonShape()
    {
        var contract = typeof(Program).GetNestedType("ISettingsSyncHelper", BindingFlags.NonPublic);
        var stateType = contract!.GetNestedType("MachineSocketState");
        var state = Activator.CreateInstance(stateType!);
        stateType!.GetField("Name")!.SetValue(state, "PC");
        stateType.GetField("Status")!.SetValue(state, Enum.ToObject(stateType.GetField("Status")!.FieldType, 9));

        Assert.AreEqual("""{"Name":"PC","Status":9}""", JsonConvert.SerializeObject(state));
    }

    [TestMethod]
    public void ConnectionRpcMethodsReturnCompletionTasks()
    {
        var contract = typeof(Program).GetNestedType("ISettingsSyncHelper", BindingFlags.NonPublic);

        Assert.AreEqual(typeof(Task), contract!.GetMethod("ConnectToMachineAsync")!.ReturnType);
        Assert.AreEqual(typeof(Task), contract.GetMethod("RestorePreviousConnectionAsync")!.ReturnType);
    }

    [TestMethod]
    public void ConnectionSnapshotRestoresMatrixAndMachineIdsAfterFailedAttempt()
    {
        var pool = new MachinePool();
        var originalNames = new[] { "REMOTE", "LOCAL" };
        pool.Initialize(originalNames);
        pool.TryUpdateMachineID("REMOTE", (ID)1, false);
        var matrix = new[] { "REMOTE", "LOCAL", string.Empty, string.Empty };
        var snapshot = new Program.ConnectionSnapshot("previous-key", matrix, pool);

        matrix[0] = "OTHER";
        var attemptedNames = new[] { "OTHER", "LOCAL" };
        pool.Initialize(attemptedNames);
        snapshot.RestoreMachinePool(pool);

        Assert.AreEqual("previous-key", snapshot.SecurityKey);
        Assert.AreEqual("REMOTE", snapshot.MachineMatrix[0]);
        Assert.IsTrue(pool.TryFindMachineByName("REMOTE", out var restored));
        Assert.AreEqual((ID)1, restored.Id);
        Assert.IsFalse(pool.TryFindMachineByName("OTHER", out _));
    }
}
