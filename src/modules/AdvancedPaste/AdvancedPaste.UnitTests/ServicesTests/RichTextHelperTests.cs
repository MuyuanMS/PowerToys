// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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
        var fragment = HtmlFormatHelper.GetStaticFragment(html);

        StringAssert.Contains(fragment, "Heading</h1>");
        StringAssert.Contains(fragment, "<strong>bold</strong>");
    }
}
