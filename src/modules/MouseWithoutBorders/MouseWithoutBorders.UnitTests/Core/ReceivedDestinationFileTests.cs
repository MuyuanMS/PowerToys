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
                using var destination = CreateDestinationFile(path);
                destination.Stream.WriteByte(1);
                throw new IOException("Simulated write failure.");
            }
            catch (IOException)
            {
            }

            Assert.IsFalse(File.Exists(path));
            Assert.AreEqual(0, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void IncompleteExistingDestinationPreservesOriginalContents()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "received.txt");
        File.WriteAllText(path, "existing");

        try
        {
            try
            {
                using var destination = CreateDestinationFile(path);
                destination.Stream.WriteByte(1);
                throw new IOException("Simulated write failure.");
            }
            catch (IOException)
            {
            }

            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual("existing", File.ReadAllText(path));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void CompleteExistingDestinationReplacesOriginalContents()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "received.txt");
        File.WriteAllText(path, "existing");

        try
        {
            using (var destination = CreateDestinationFile(path))
            {
                destination.Stream.WriteByte(1);
                destination.Complete();
            }

            CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(path));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ReceivedDestinationFile CreateDestinationFile(string path)
    {
        return new ReceivedDestinationFile(path, File.Delete, (source, destination) => File.Move(source, destination, overwrite: true));
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        return directory;
    }
}
