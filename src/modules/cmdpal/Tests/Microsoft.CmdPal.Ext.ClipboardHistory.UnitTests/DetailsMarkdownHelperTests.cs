// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;
using Microsoft.CmdPal.Ext.ClipboardHistory.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.ClipboardHistory.UnitTests;

[TestClass]
public class DetailsMarkdownHelperTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void EscapeMarkdown_ReturnsEmpty_WhenTextIsNullOrEmpty(string text)
    {
        var result = DetailsMarkdownHelper.EscapeMarkdown(text);

        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void EscapeMarkdown_ReturnsPlainText_Unchanged()
    {
        var result = DetailsMarkdownHelper.EscapeMarkdown("plain clipboard text");

        Assert.AreEqual("plain clipboard text", result);
    }

    [TestMethod]
    [DataRow("*bold*", "\\*bold\\*")]
    [DataRow("# heading", "\\# heading")]
    [DataRow("- list item", "\\- list item")]
    [DataRow("[link](url)", "\\[link\\]\\(url\\)")]
    [DataRow("`code`", "\\`code\\`")]
    [DataRow("a > b", "a \\> b")]
    public void EscapeMarkdown_EscapesMarkdownSyntaxCharacters(string input, string expected)
    {
        var result = DetailsMarkdownHelper.EscapeMarkdown(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void BuildImageBodyFromBytes_ReturnsFullWidthMarkdownImage_WithFitHint()
    {
        var bytes = Encoding.UTF8.GetBytes("fake-png-bytes");

        var body = DetailsMarkdownHelper.BuildImageBodyFromBytes(bytes, "Image");

        var expectedBase64 = System.Convert.ToBase64String(bytes);
        Assert.AreEqual($"![Image](data:image/png;base64,{expectedBase64}#--x-cmdpal-fit=fit)", body);
    }

    [TestMethod]
    public void BuildImageBodyFromBytes_UsesProvidedAltText()
    {
        var bytes = Encoding.UTF8.GetBytes("bytes");

        var body = DetailsMarkdownHelper.BuildImageBodyFromBytes(bytes, "Clipboard image");

        StringAssert.StartsWith(body, "![Clipboard image](data:image/png;base64,");
    }

    [TestMethod]
    public void BuildImageBody_ReturnsEmpty_WhenImageDataIsNull()
    {
        var body = DetailsMarkdownHelper.BuildImageBody(null, "Image");

        Assert.AreEqual(string.Empty, body);
    }
}
