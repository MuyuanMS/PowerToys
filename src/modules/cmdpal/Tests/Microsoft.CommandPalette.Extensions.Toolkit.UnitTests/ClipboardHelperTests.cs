// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Microsoft.CommandPalette.Extensions.Toolkit.UnitTests;

[TestClass]
[DoNotParallelize]
public class ClipboardHelperTests
{
    [TestMethod]
    public void SetText_RoundTripsThroughClipboard()
    {
        var value = $"Command Palette clipboard test {Guid.NewGuid():N}";

        ClipboardHelper.SetText(value);

        Assert.AreEqual(value, ClipboardHelper.GetText());
    }

    [TestMethod]
    public void SetImage_RejectsNullReference()
    {
        Assert.ThrowsException<ArgumentNullException>(() => ClipboardHelper.SetImage(null!));
    }

    [TestMethod]
    public void SetContent_RejectsNullPackage()
    {
        Assert.ThrowsException<ArgumentNullException>(() => ClipboardHelper.SetContent(null!));
    }

    [TestMethod]
    public async Task SetImage_RoundTripsThroughClipboard()
    {
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(pngBytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        stream.Seek(0);

        ClipboardHelper.SetImage(RandomAccessStreamReference.CreateFromStream(stream));

        var bitmap = await Clipboard.GetContent().GetBitmapAsync();
        Assert.IsNotNull(bitmap);
    }
}
