// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;

using AdvancedPaste.Helpers;
using AdvancedPaste.Models;
using AdvancedPaste.UnitTests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.ApplicationModel.DataTransfer;

namespace AdvancedPaste.UnitTests.ServicesTests;

[TestClass]
public sealed class RichTextHelperTests
{
    [TestMethod]
    public async Task TransformAsync_WithMarkdownText_CreatesHtmlClipboardPayload()
    {
        DataPackage input = new();
        input.SetText("# Heading\n\nThis is **bold**.");

        var output = await TransformHelpers.TransformAsync(
            PasteFormats.RichText,
            input.GetView(),
            CancellationToken.None,
            new NoOpProgress());

        var html = await output.GetView().GetHtmlFormatAsync();

        StringAssert.Contains(html, "Heading</h1>");
        StringAssert.Contains(html, "<strong>bold</strong>");
    }
}
