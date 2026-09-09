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
        const string primary = "relative-primary.png";
        const string fallback = "relative-fallback.png";
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
            null,
            64,
            ElementTheme.Default);

        CollectionAssert.AreEqual(
            new[] { (primary, true), (fallback, true) },
            attempts);
        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.BitmapStream, result.Kind);
        Assert.AreSame(stream, result.BitmapStream);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UsesPreparedPrimaryBeforeLaterThumbnailFallback(bool jumbo)
    {
        var primary = $"{GetShell32DllPath()},1";
        var fallback = "C:\\Program Files\\Example\\app.exe";
        var attempts = new List<(string Candidate, bool Jumbo)>();
        var processor = new AppIconProtocolProcessor((candidate, requestedJumbo) =>
        {
            attempts.Add((candidate, requestedJumbo));
            return candidate == fallback
                ? Task.FromResult<IRandomAccessStream?>(new InMemoryRandomAccessStream())
                : Task.FromResult<IRandomAccessStream?>(null);
        });

        var iconDescription = jumbo
            ? AppIconProtocol.CreateJumbo(primary, fallback)
            : AppIconProtocol.Create(primary, fallback);
        using var result = await processor.PrepareAsync(iconDescription, null, 20, ElementTheme.Default);

        CollectionAssert.AreEqual(Array.Empty<(string Candidate, bool Jumbo)>(), attempts);
        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.PreparedIcon, result.Kind);
        using var prepared = result.TakePreparedIcon();
        Assert.IsNotNull(prepared);
        Assert.AreEqual(IconPathConverter.PreparedIconKind.Binary, prepared.Kind);
        Assert.IsNotNull(prepared.SoftwareBitmap);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(5_000)]
    public async Task MissingExecutableUsesIndexedFallbackAfterThumbnailMisses(bool jumbo)
    {
        var primary = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");
        var fallback = $"{GetShell32DllPath()},1";
        var attempts = new List<(string Candidate, bool Jumbo)>();
        var processor = new AppIconProtocolProcessor(
            (candidate, requestedJumbo) =>
            {
                attempts.Add((candidate, requestedJumbo));
                return Task.FromResult<IRandomAccessStream?>(null);
            });

        using var result = await processor.PrepareAsync(
            jumbo ? AppIconProtocol.CreateJumbo(primary, fallback) : AppIconProtocol.Create(primary, fallback),
            null,
            32,
            ElementTheme.Default);

        CollectionAssert.AreEqual(Array.Empty<(string Candidate, bool Jumbo)>(), attempts);
        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.PreparedIcon, result.Kind);
        using var prepared = result.TakePreparedIcon();
        Assert.IsNotNull(prepared);
        Assert.AreEqual(IconPathConverter.PreparedIconKind.Binary, prepared.Kind);
        Assert.IsNotNull(prepared.SoftwareBitmap);
        Assert.IsTrue(prepared.SoftwareBitmap.PixelWidth > 0);
        Assert.IsTrue(prepared.SoftwareBitmap.PixelHeight > 0);
    }

    [TestMethod]
    public async Task MissingImageFileUsesCustomFontGlyphFallback()
    {
        var primary = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ico");
        const string fallback = "\uE737";
        var processor = new AppIconProtocolProcessor(static (_, _) => Task.FromResult<IRandomAccessStream?>(null));

        using var result = await processor.PrepareAsync(
            AppIconProtocol.Create(primary, fallback),
            "Custom Font",
            24,
            ElementTheme.Default);

        Assert.AreEqual(IconProtocolProcessingResult.ResultKind.PreparedIcon, result.Kind);
        using var prepared = result.TakePreparedIcon();
        Assert.IsNotNull(prepared);
        Assert.AreEqual(IconPathConverter.PreparedIconKind.Glyph, prepared.Kind);
        Assert.AreEqual("Custom Font", prepared.FontFamily);
        Assert.AreEqual(fallback, prepared.Glyph);
    }

    private static string GetShell32DllPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
    }
}
