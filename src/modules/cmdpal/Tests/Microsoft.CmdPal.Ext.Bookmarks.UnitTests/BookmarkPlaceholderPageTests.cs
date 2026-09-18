// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CmdPal.Ext.Bookmarks.Helpers;
using Microsoft.CmdPal.Ext.Bookmarks.Pages;
using Microsoft.CmdPal.Ext.Bookmarks.Persistence;
using Microsoft.CmdPal.Ext.Bookmarks.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.Bookmarks.UnitTests;

[TestClass]
public sealed class BookmarkPlaceholderPageTests
{
    private sealed class TestBookmarkResolver : IBookmarkResolver
    {
        public Task<(bool Success, Classification Result)> TryClassifyAsync(string input, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, Classification.Unknown(input)));

        public Classification ClassifyOrUnknown(string input) => Classification.Unknown(input);
    }

    private sealed class TestBookmarkIconLocator : IBookmarkIconLocator
    {
        public Task<IIconInfo> GetIconForPath(Classification classification, CancellationToken cancellationToken = default) =>
            Task.FromResult<IIconInfo>(null);
    }

    [TestMethod]
    public void ResetPlaceholderValues_ClearsAllUniquePlaceholderValues()
    {
        using var page = CreatePage("https://example.com/{id}/{project}/{id}");

        var parameterOccurrences = page.Parameters.OfType<StringParameterRun>().ToArray();
        Assert.AreEqual(3, parameterOccurrences.Length);
        Assert.AreSame(parameterOccurrences[0], parameterOccurrences[2]);

        var parameters = parameterOccurrences.Distinct().ToArray();
        Assert.AreEqual(2, parameters.Length);
        parameters[0].Text = "42";
        parameters[1].Text = "PowerToys";

        page.ResetPlaceholderValues();

        Assert.AreEqual(string.Empty, parameters[0].Text);
        Assert.AreEqual(string.Empty, parameters[1].Text);
    }

    [TestMethod]
    public void Invoke_ClearsPlaceholderValuesAfterSuccessfulLaunch()
    {
        using var page = CreatePage(
            "https://example.com/{id}/{project}/{id}",
            _ => true);
        var parameters = page.Parameters.OfType<StringParameterRun>().Distinct().ToArray();
        parameters[0].Text = "42";
        parameters[1].Text = "PowerToys";

        var result = ((IInvokableCommand)page.Command.Command).Invoke(null);

        Assert.AreEqual(CommandResultKind.Dismiss, result.Kind);
        Assert.AreEqual(string.Empty, parameters[0].Text);
        Assert.AreEqual(string.Empty, parameters[1].Text);
    }

    [TestMethod]
    public void Invoke_KeepsPlaceholderValuesAfterFailedLaunch()
    {
        using var page = CreatePage(
            "https://example.com/{id}/{project}/{id}",
            _ => false);
        var parameters = page.Parameters.OfType<StringParameterRun>().Distinct().ToArray();
        parameters[0].Text = "42";
        parameters[1].Text = "PowerToys";

        var result = ((IInvokableCommand)page.Command.Command).Invoke(null);

        Assert.AreEqual(CommandResultKind.KeepOpen, result.Kind);
        Assert.AreEqual("42", parameters[0].Text);
        Assert.AreEqual("PowerToys", parameters[1].Text);
    }

    private static BookmarkPlaceholderPage CreatePage(string bookmarkAddress, Func<Classification, bool> launchBookmark = null)
    {
        var bookmark = new BookmarkData("Test bookmark", bookmarkAddress);

        return new BookmarkPlaceholderPage(
            bookmark,
            new TestBookmarkIconLocator(),
            new TestBookmarkResolver(),
            new PlaceholderParser(),
            launchBookmark ?? (_ => true));
    }
}
