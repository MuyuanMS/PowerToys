// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public class TopLevelCommandResolverTests
{
    [TestMethod]
    public void Resolve_UsesPinOrderAndSkipsUnavailableOrIneligibleCommands()
    {
        var pins = new[]
        {
            new PinnedCommandSettings("provider-b", "second"),
            new PinnedCommandSettings("missing", "command"),
            new PinnedCommandSettings("provider-a", "first"),
            new PinnedCommandSettings("provider-a", "hidden"),
        };
        var commands = new[]
        {
            new TestCommand("provider-a", "first", IsEligible: true),
            new TestCommand("provider-a", "hidden", IsEligible: false),
            new TestCommand("provider-b", "second", IsEligible: true),
        };

        var sections = TopLevelCommandResolver.Resolve(
            pins,
            [],
            commands,
            static command => command.ProviderId,
            static command => command.CommandId,
            static command => command.IsEligible);

        CollectionAssert.AreEqual(new[] { commands[2], commands[0] }, sections.Pinned.ToArray());
        Assert.AreEqual(0, sections.Recent.Count);
        Assert.AreEqual(0, sections.Regular.Count);
    }

    [TestMethod]
    public void Resolve_PinnedLimitCountsResolvedPinsAndKeepsDroppedPinsReserved()
    {
        var pins = new[]
        {
            new PinnedCommandSettings("missing", "missing"),
            new PinnedCommandSettings("provider-a", "first"),
            new PinnedCommandSettings("provider-b", "second"),
        };
        var commands = new[]
        {
            new TestCommand("provider-a", "first", IsEligible: true),
            new TestCommand("provider-b", "second", IsEligible: true),
        };

        var sections = TopLevelCommandResolver.Resolve(
            pins,
            [new(null, "second"), new(null, "first")],
            commands,
            static command => command.ProviderId,
            static command => command.CommandId,
            static command => command.IsEligible,
            pinnedCommandLimit: 1,
            recentCommandLimit: 2);

        CollectionAssert.AreEqual(new[] { commands[0] }, sections.Pinned.ToArray());
        Assert.AreEqual(0, sections.Recent.Count);
        Assert.AreEqual(0, sections.Regular.Count);
    }

    [TestMethod]
    public void Resolve_RecentPlacementDoesNotChangePinnedOwnership()
    {
        var pins = new[]
        {
            new PinnedCommandSettings("provider-a", "recent-pin"),
            new PinnedCommandSettings("provider-b", "second"),
            new PinnedCommandSettings("provider-c", "third"),
        };
        var commands = new[]
        {
            new TestCommand("provider-a", "recent-pin", IsEligible: true),
            new TestCommand("provider-b", "second", IsEligible: true),
            new TestCommand("provider-c", "third", IsEligible: true),
            new TestCommand("provider-d", "recent-only", IsEligible: true),
        };

        var sections = TopLevelCommandResolver.Resolve(
            pins,
            [new("provider-a", "recent-pin"), new("provider-d", "recent-only")],
            commands,
            static command => command.ProviderId,
            static command => command.CommandId,
            static command => command.IsEligible,
            pinnedCommandLimit: 2,
            recentCommandLimit: 2);

        CollectionAssert.AreEqual(new[] { commands[0], commands[1] }, sections.Pinned.ToArray());
        CollectionAssert.AreEqual(new[] { commands[3] }, sections.Recent.ToArray());
        Assert.AreEqual(0, sections.Regular.Count);
    }

    [TestMethod]
    public void Resolve_RecentCommandsFollowHistoryAndExcludePinsAndMissingCommands()
    {
        var commands = new[]
        {
            new TestCommand("provider-e", "pinned", IsEligible: true),
            new TestCommand("provider-a", "pinned", IsEligible: true),
            new TestCommand("provider-b", "older", IsEligible: true),
            new TestCommand("provider-c", "newer", IsEligible: true),
            new TestCommand("provider-d", "regular", IsEligible: true),
        };

        var sections = TopLevelCommandResolver.Resolve(
            [new PinnedCommandSettings("provider-a", "pinned")],
            [
                new("provider-a", "pinned"),
                new(null, "missing"),
                new("provider-c", "newer"),
                new("provider-b", "older"),
                new("provider-d", "regular"),
            ],
            commands,
            static command => command.ProviderId,
            static command => command.CommandId,
            static command => command.IsEligible,
            recentCommandLimit: 2);

        CollectionAssert.AreEqual(new[] { commands[1] }, sections.Pinned.ToArray());
        CollectionAssert.AreEqual(new[] { commands[3], commands[2] }, sections.Recent.ToArray());
        CollectionAssert.AreEqual(new[] { commands[0], commands[4] }, sections.Regular.ToArray());
    }

    [TestMethod]
    public void Resolve_UsesAdditionalResolverForRecentItemsWithoutAddingThemToRegularCommands()
    {
        var regular = new TestCommand("provider-a", "regular", IsEligible: true);
        var recentApp = new TestCommand("AllApps", "recent-app", IsEligible: true);

        var sections = TopLevelCommandResolver.Resolve(
            [],
            [new(null, "missing"), new("AllApps", "recent-app")],
            [regular],
            static command => command.ProviderId,
            static command => command.CommandId,
            static command => command.IsEligible,
            commandId => commandId == recentApp.CommandId ? recentApp : null);

        CollectionAssert.AreEqual(new[] { recentApp }, sections.Recent.ToArray());
        CollectionAssert.AreEqual(new[] { regular }, sections.Regular.ToArray());
    }

    [TestMethod]
    public void Resolve_ProviderQualifiedHistorySelectsMatchingProviderOnIdCollision()
    {
        var first = new TestCommand("provider-a", "shared", IsEligible: true);
        var second = new TestCommand("provider-b", "shared", IsEligible: true);

        var sections = TopLevelCommandResolver.Resolve(
            [],
            [new RecentCommandIdentity("provider-b", "shared")],
            [first, second],
            static command => command.ProviderId,
            static command => command.CommandId,
            static command => command.IsEligible);

        CollectionAssert.AreEqual(new[] { second }, sections.Recent.ToArray());
        CollectionAssert.AreEqual(new[] { first }, sections.Regular.ToArray());
    }

    [TestMethod]
    public void Resolve_LegacyIdOnlyHistoryStillResolvesFirstMatchingCommand()
    {
        var first = new TestCommand("provider-a", "shared", IsEligible: true);
        var second = new TestCommand("provider-b", "shared", IsEligible: true);

        var sections = TopLevelCommandResolver.Resolve(
            [],
            [new RecentCommandIdentity(null, "shared")],
            [first, second],
            static command => command.ProviderId,
            static command => command.CommandId,
            static command => command.IsEligible);

        CollectionAssert.AreEqual(new[] { first }, sections.Recent.ToArray());
        CollectionAssert.AreEqual(new[] { second }, sections.Regular.ToArray());
    }

    [TestMethod]
    public void Resolve_UsesFirstPassKeyForRegularCommands()
    {
        var command = new TestCommand("provider-a", "command", IsEligible: true);
        var providerIdReads = 0;

        var sections = TopLevelCommandResolver.Resolve(
            [new PinnedCommandSettings("provider-a", "command")],
            [],
            [command],
            _ => ++providerIdReads == 1 ? "provider-a" : "provider-b",
            static command => command.CommandId,
            static command => command.IsEligible);

        Assert.AreEqual(1, providerIdReads);
        CollectionAssert.AreEqual(new[] { command }, sections.Pinned.ToArray());
        Assert.AreEqual(0, sections.Regular.Count);
    }

    [DataTestMethod]
    [DataRow(false, "Command", true)]
    [DataRow(true, "Command", false)]
    [DataRow(false, "", false)]
    [DataRow(false, null, false)]
    public void IsEligibleForHome_ExcludesFallbacksAndUntitledCommands(bool isFallback, string? title, bool expected)
    {
        Assert.AreEqual(expected, TopLevelCommandEligibility.IsEligibleForHome(isFallback, title));
    }

    private sealed record TestCommand(string ProviderId, string CommandId, bool IsEligible);
}
