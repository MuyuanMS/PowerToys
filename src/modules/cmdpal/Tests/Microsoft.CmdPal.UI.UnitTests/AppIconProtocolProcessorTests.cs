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
    public async Task TriesCandidatesInOrderUntilThumbnailSucceeds()
    {
        const string primary = "C:\\Icons\\missing.ico";
        const string fallback = "steam://run/123|variant";
        var attempts = new List<(string Candidate, bool Jumbo)>();
        var stream = new InMemoryRandomAccessStream();
        var processor = new AppIconProtocolProcessor((candidate, jumbo) =>
        {
            attempts.Add((candidate, jumbo));
            return candidate == primary
                ? Task.FromException<IRandomAccessStream?>(new IOException("Primary failed"))
                : Task.FromResult<IRandomAccessStream?>(stream);
        });

        using var result = await processor.PrepareAsync(
            AppIconProtocol.CreateJumbo(primary, fallback),
            64,
            ElementTheme.Default);

        CollectionAssert.AreEqual(
            new[] { (primary, true), (fallback, true) },
            attempts);
        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.BitmapStream, result.Kind);
        Assert.AreSame(stream, result.BitmapStream);
    }

    [TestMethod]
    public async Task FallsBackToPrimaryAfterEveryThumbnailMisses()
    {
        const string primary = "C:\\Icons\\primary.ico";
        const string fallback = "C:\\Program Files\\Example\\app.exe";
        var processor = new AppIconProtocolProcessor(
            (_, _) => Task.FromResult<IRandomAccessStream?>(null));

        using var result = await processor.PrepareAsync(
            AppIconProtocol.Create(primary, fallback),
            20,
            ElementTheme.Default);

        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.FallbackIconString, result.Kind);
        Assert.AreEqual(primary, result.FallbackIconString);
    }

    [TestMethod]
    public async Task FallsBackToLaterOrdinaryIconCandidateAfterThumbnailMisses()
    {
        const string primary = "C:\\Icons\\primary.ico";
        const string fallback = "ms-appx:///Assets/icon.svg";
        var processor = new AppIconProtocolProcessor(
            (_, _) => Task.FromResult<IRandomAccessStream?>(null));

        using var result = await processor.PrepareAsync(
            AppIconProtocol.Create(primary, fallback),
            20,
            ElementTheme.Default);

        using var prepared = result.TakePreparedIcon();

        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.PreparedIcon, result.Kind);
        Assert.IsNotNull(prepared);
        Assert.AreEqual(IconPathConverter.PreparedIconKind.SvgUri, prepared.Kind);
        Assert.AreEqual(fallback, prepared.Uri?.OriginalString);
    }

    [TestMethod]
    public async Task SkipsUnsupportedUriFallbackCandidateAfterThumbnailMisses()
    {
        const string primary = "C:\\Icons\\primary.ico";
        const string unsupportedFallback = "steam://run/123|variant";
        const string supportedFallback = "ms-appx:///Assets/icon.svg";
        var processor = new AppIconProtocolProcessor(
            (_, _) => Task.FromResult<IRandomAccessStream?>(null));

        using var result = await processor.PrepareAsync(
            AppIconProtocol.Create(primary, unsupportedFallback, supportedFallback),
            20,
            ElementTheme.Default);

        using var prepared = result.TakePreparedIcon();

        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.PreparedIcon, result.Kind);
        Assert.IsNotNull(prepared);
        Assert.AreEqual(IconPathConverter.PreparedIconKind.SvgUri, prepared.Kind);
        Assert.AreEqual(supportedFallback, prepared.Uri?.OriginalString);
    }

    [TestMethod]
    public async Task SkipsInvalidGlyphFallbackCandidateAfterThumbnailMisses()
    {
        const string primary = "C:\\Icons\\primary.ico";
        const string invalidGlyph = "not a glyph";
        const string validGlyph = "\uE700";
        var processor = new AppIconProtocolProcessor(
            (_, _) => Task.FromResult<IRandomAccessStream?>(null));

        using var result = await processor.PrepareAsync(
            AppIconProtocol.Create(primary, invalidGlyph, validGlyph),
            20,
            ElementTheme.Default);

        using var prepared = result.TakePreparedIcon();

        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.PreparedIcon, result.Kind);
        Assert.IsNotNull(prepared);
        Assert.AreEqual(IconPathConverter.PreparedIconKind.Glyph, prepared.Kind);
        Assert.AreEqual(validGlyph, prepared.Glyph);
    }
}
