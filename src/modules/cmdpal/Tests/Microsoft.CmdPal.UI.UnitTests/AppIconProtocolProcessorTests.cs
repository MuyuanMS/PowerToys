// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.UI.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Storage.Streams;

namespace Microsoft.CmdPal.UI.UnitTests;

[TestClass]
public class AppIconProtocolProcessorTests
{
    [TestMethod]
    public async Task UsesThumbnailFromFirstCandidateWhenAvailable()
    {
        const string primary = "C:\\Icons\\missing.ico";
        const string fallback = "steam://run/123|variant";
        var attempts = new List<(string Candidate, bool Jumbo)>();
        var stream = new InMemoryRandomAccessStream();
        var processor = new AppIconProtocolProcessor((candidate, jumbo) =>
        {
            attempts.Add((candidate, jumbo));
            return Task.FromResult<IRandomAccessStream?>(stream);
        });

        using var result = await processor.PrepareAsync(
            AppIconProtocol.CreateJumbo(primary, fallback),
            64,
            ElementTheme.Default);

        CollectionAssert.AreEqual(
            new[] { (primary, true) },
            attempts);
        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.BitmapStream, result.Kind);
        Assert.AreSame(stream, result.BitmapStream);
    }

    [TestMethod]
    public async Task PreservesCandidateOrderAfterFirstThumbnailMiss()
    {
        const string primary = "C:\\Icons\\primary.ico";
        const string fallback = "ms-appx:///Assets/fallback.png";
        var attempts = new List<(string Candidate, bool Jumbo)>();
        var processor = new AppIconProtocolProcessor(
            (candidate, jumbo) =>
            {
                attempts.Add((candidate, jumbo));
                return Task.FromResult<IRandomAccessStream?>(null);
            });

        using var result = await processor.PrepareAsync(
            AppIconProtocol.CreateJumbo(primary, fallback),
            20,
            ElementTheme.Default);

        CollectionAssert.AreEqual(
            new[] { (primary, true) },
            attempts);
        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.FallbackIconStrings, result.Kind);
        CollectionAssert.AreEqual(new[] { primary, fallback }, result.FallbackIconStrings.ToArray());
    }

    [TestMethod]
    public async Task PreservesCandidateOrderWhenThumbnailThrows()
    {
        const string primary = "C:\\Icons\\primary.ico";
        const string fallback = "ms-appx:///Assets/fallback.png";
        var processor = new AppIconProtocolProcessor(
            (_, _) => Task.FromException<IRandomAccessStream?>(new IOException("Primary failed")));

        using var result = await processor.PrepareAsync(
            AppIconProtocol.Create(primary, fallback),
            20,
            ElementTheme.Default);

        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.FallbackIconStrings, result.Kind);
        CollectionAssert.AreEqual(new[] { primary, fallback }, result.FallbackIconStrings.ToArray());
    }
}
