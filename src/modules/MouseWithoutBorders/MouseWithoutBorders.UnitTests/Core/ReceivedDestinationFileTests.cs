// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class ReceivedDestinationFileTests
{
    [TestMethod]
    public void ExecuteImpersonatedActionRevertsWhenCallbackThrows()
    {
        var reverted = false;

        Assert.ThrowsException<InvalidOperationException>(() => Launch.ExecuteImpersonatedAction(
            () => throw new InvalidOperationException(),
            () => reverted = true));

        Assert.IsTrue(reverted);
    }

    [TestMethod]
    public void IncompleteNewDestinationIsDeletedAfterWriteFailure()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "received.txt");

        try
        {
            try
            {
                using var destination = new ReceivedDestinationFile(path, File.Delete);
                destination.Stream.WriteByte(1);
                throw new IOException("Simulated write failure.");
            }
            catch (IOException)
            {
            }

            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void IncompleteExistingDestinationIsNotDeletedAfterWriteFailure()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "received.txt");
        File.WriteAllText(path, "existing");

        try
        {
            try
            {
                using var destination = new ReceivedDestinationFile(path, File.Delete);
                destination.Stream.WriteByte(1);
                throw new IOException("Simulated write failure.");
            }
            catch (IOException)
            {
            }

            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(1L, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        return directory;
    }
}
