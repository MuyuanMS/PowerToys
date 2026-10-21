// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using AdvancedPaste.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.ApplicationModel.DataTransfer;

namespace AdvancedPaste.UnitTests.ServicesTests;

[TestClass]
public class RichTextHelperTests
{
    [TestMethod]
    public void ToRichText_ConvertsMarkdownToHtml()
    {
        var markdown = "**Bold** and *italic* text";

        var html = RichTextHelper.ToRichText(markdown);

        Assert.IsTrue(html.Contains("<strong>Bold</strong>", StringComparison.Ordinal), "Missing bold tag");
        Assert.IsTrue(html.Contains("<em>italic</em>", StringComparison.Ordinal), "Missing italic tag");
    }

    [TestMethod]
    public void ToRichText_ReturnsEmptyString_WhenEmpty()
    {
        var html = RichTextHelper.ToRichText(string.Empty);

        Assert.AreEqual(string.Empty, html);
    }

    [TestMethod]
    public void ToRichText_EncodesRawHtml()
    {
        var html = RichTextHelper.ToRichText("<script>alert('x')</script>");

        Assert.IsFalse(html.Contains("<script>", StringComparison.Ordinal), "Raw HTML should not be emitted");
        Assert.IsTrue(html.Contains("&lt;script&gt;", StringComparison.Ordinal), "Raw HTML should be rendered as text");
    }

    [TestMethod]
    public void ToRichText_RemovesUnsafeLinksAndImages()
    {
        const string markdown =
            "[script](javascript:alert(1)) " +
            "[file](file:///C:/secret) [data](data:text/html,hello) " +
            "<javascript:alert(1)> ![tracking pixel](https://example.com/pixel.png)";

        var html = RichTextHelper.ToRichText(markdown);

        Assert.IsFalse(html.Contains("href=", StringComparison.Ordinal), "Unsafe link targets should be removed");
        Assert.IsFalse(html.Contains("<img", StringComparison.Ordinal), "Images should not cause remote content to load");
        Assert.IsTrue(html.Contains("script", StringComparison.Ordinal), "Link text should be preserved");
        Assert.IsTrue(html.Contains("file", StringComparison.Ordinal), "Link text should be preserved");
        Assert.IsTrue(html.Contains("data", StringComparison.Ordinal), "Link text should be preserved");
        Assert.IsTrue(html.Contains("tracking pixel", StringComparison.Ordinal), "Image alt text should be preserved");
    }

    [TestMethod]
    public void ToRichText_PreservesAllowlistedLinks()
    {
        const string markdown = "[site](https://example.com) [email](mailto:user@example.com)";

        var html = RichTextHelper.ToRichText(markdown);

        Assert.IsTrue(html.Contains("href=\"https://example.com\"", StringComparison.Ordinal), "HTTPS links should be preserved");
        Assert.IsTrue(html.Contains("href=\"mailto:user@example.com\"", StringComparison.Ordinal), "Mail links should be preserved");
    }

    [TestMethod]
    public void ToRichText_ProducesValidHtmlFragment()
    {
        var markdown = "# Heading\n\n**Bold** text";

        var html = RichTextHelper.ToRichText(markdown);
        var cfHtml = HtmlFormatHelper.CreateHtmlFormat(html);
        var fragment = HtmlFormatHelper.GetStaticFragment(cfHtml);

        Assert.IsTrue(fragment.Contains("<h1", StringComparison.Ordinal), "Missing heading in fragment");
        Assert.IsTrue(fragment.Contains("<strong>Bold</strong>", StringComparison.Ordinal), "Missing bold in fragment");
        Assert.IsFalse(fragment.Contains("Version:", StringComparison.Ordinal), "Fragment should not contain CF_HTML headers");
        Assert.IsFalse(fragment.Contains("StartFragment:", StringComparison.Ordinal), "Fragment should not contain StartFragment header");
    }

    [TestMethod]
    public async Task CreateFromHtml_IncludesHtmlAndPlainTextFallback()
    {
        var rawText = "**Bold**";
        var package = DataPackageHelpers.CreateFromHtml("<strong>Bold</strong>", rawText);
        var view = package.GetView();

        Assert.IsTrue(view.Contains(StandardDataFormats.Html), "Package should contain HTML format");
        Assert.IsTrue(view.Contains(StandardDataFormats.Text), "Package should contain Text format fallback");
        Assert.AreEqual(rawText, await view.GetTextAsync());
    }

    [TestMethod]
    public async Task CreateFromHtml_UsesTextOnlyFallback_WhenHtmlIsEmpty()
    {
        const string rawText = "plain text";
        var package = DataPackageHelpers.CreateFromHtml(string.Empty, rawText);
        var view = package.GetView();

        Assert.IsFalse(view.Contains(StandardDataFormats.Html), "Package should omit empty HTML");
        Assert.IsTrue(view.Contains(StandardDataFormats.Text), "Package should retain the text fallback");
        Assert.AreEqual(rawText, await view.GetTextAsync());
    }
}
