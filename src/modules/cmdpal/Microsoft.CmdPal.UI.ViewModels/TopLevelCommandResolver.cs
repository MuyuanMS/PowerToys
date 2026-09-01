// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.Ext.Apps;
using Microsoft.CommandPalette.Extensions;

namespace Microsoft.CmdPal.UI.ViewModels;

internal static class TopLevelCommandResolver
{
    internal sealed record Sections<TCommand>(
        IReadOnlyList<TCommand> Pinned,
        IReadOnlyList<TCommand> Recent,
        IReadOnlyList<TCommand> Regular);

    internal static Sections<IListItem> Resolve(
        IEnumerable<PinnedCommandSettings> pinnedCommands,
        IEnumerable<RecentCommandIdentity> recentCommands,
        IEnumerable<TopLevelViewModel> availableCommands,
        bool includeApps,
        int pinnedCommandLimit = int.MaxValue,
        int recentCommandLimit = SettingsModel.DefaultRecentCommandsDisplayLimit,
        bool includeRegular = true)
    {
        static IListItem? ResolveRecentApp(string commandId) =>
            AllAppsCommandProvider.Page.TryGetCurrentItem(commandId, out var item) ? item : null;

        Func<string, IListItem?>? additionalRecentResolver = includeApps ? ResolveRecentApp : null;
        return Resolve<IListItem>(
            pinnedCommands,
            recentCommands,
            availableCommands,
            GetProviderId,
            GetCommandId,
            IsEligibleForHome,
            additionalRecentResolver,
            pinnedCommandLimit,
            recentCommandLimit,
            includeRegular);
    }

    internal static string GetProviderId(IListItem command) =>
        command is TopLevelViewModel topLevel ? topLevel.CommandProviderId : AllAppsCommandProvider.WellKnownId;

    internal static string GetCommandId(IListItem command) =>
        command is TopLevelViewModel topLevel ? topLevel.Id : command.Command?.Id ?? string.Empty;

    internal static bool IsEligibleForHome(IListItem command) =>
        command is TopLevelViewModel topLevel
            ? TopLevelCommandEligibility.IsEligibleForHome(topLevel)
            : command.Command is not null && !string.IsNullOrEmpty(command.Title);

    internal static Sections<TCommand> Resolve<TCommand>(
        IEnumerable<PinnedCommandSettings> pinnedCommands,
        IEnumerable<RecentCommandIdentity> recentCommands,
        IEnumerable<TCommand> availableCommands,
        Func<TCommand, string> providerIdSelector,
        Func<TCommand, string> commandIdSelector,
        Func<TCommand, bool> isEligible,
        Func<string, TCommand?>? resolveAdditionalRecentCommand = null,
        int pinnedCommandLimit = int.MaxValue,
        int recentCommandLimit = SettingsModel.DefaultRecentCommandsDisplayLimit,
        bool includeRegular = true)
        where TCommand : class
    {
        var eligibleCommands = new List<(TCommand Command, (string ProviderId, string CommandId) Key)>();
        var commandsByProviderAndId = new Dictionary<(string ProviderId, string CommandId), TCommand>();
        var commandsById = new Dictionary<string, TCommand>(StringComparer.Ordinal);

        foreach (var command in availableCommands)
        {
            if (!isEligible(command))
            {
                continue;
            }

            var providerId = providerIdSelector(command);
            var commandId = commandIdSelector(command);
            var key = (providerId, commandId);
            if (includeRegular)
            {
                eligibleCommands.Add((command, key));
            }

            commandsByProviderAndId.TryAdd(key, command);
            if (!string.IsNullOrEmpty(commandId))
            {
                commandsById.TryAdd(commandId, command);
            }
        }

        var featuredCommandKeys = new HashSet<(string ProviderId, string CommandId)>();
        var pinned = new List<TCommand>();
        var recent = new List<TCommand>();

        void ResolvePinnedCommands()
        {
            var effectivePinnedCommandLimit = Math.Max(0, pinnedCommandLimit);
            foreach (var pinnedCommand in pinnedCommands)
            {
                var key = (pinnedCommand.ProviderId, pinnedCommand.CommandId);
                if (commandsByProviderAndId.TryGetValue(key, out var command) && featuredCommandKeys.Add(key))
                {
                    if (pinned.Count < effectivePinnedCommandLimit)
                    {
                        pinned.Add(command);
                    }
                }
            }
        }

        void ResolveRecentCommands()
        {
            if (recentCommandLimit <= 0)
            {
                return;
            }

            var ambiguousLegacyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var recentCommand in recentCommands)
            {
                if (recent.Count == recentCommandLimit)
                {
                    break;
                }

                var commandId = recentCommand.CommandId;
                if (string.IsNullOrEmpty(commandId))
                {
                    continue;
                }

                TCommand? command;
                if (recentCommand.IsProviderQualified)
                {
                    var recentKey = (recentCommand.ProviderId!, commandId);
                    if (featuredCommandKeys.Contains(recentKey))
                    {
                        continue;
                    }

                    commandsByProviderAndId.TryGetValue(recentKey, out command);
                    if (command is null &&
                        recentCommand.ProviderId == AllAppsCommandProvider.WellKnownId)
                    {
                        command = resolveAdditionalRecentCommand?.Invoke(commandId);
                    }
                }
                else
                {
                    if (ambiguousLegacyIds.Contains(commandId) ||
                        featuredCommandKeys.Any(key => key.CommandId == commandId))
                    {
                        continue;
                    }

                    commandsById.TryGetValue(commandId, out command);
                    command ??= resolveAdditionalRecentCommand?.Invoke(commandId);
                    ambiguousLegacyIds.Add(commandId);
                }

                if (command is null || !isEligible(command))
                {
                    continue;
                }

                var key = (providerIdSelector(command), commandIdSelector(command));
                if (featuredCommandKeys.Add(key))
                {
                    recent.Add(command);
                }
            }
        }

        // Pins always own duplicates. Presentation order is applied after resolution so moving
        // the recent section does not turn pinned commands into recent commands.
        ResolvePinnedCommands();
        ResolveRecentCommands();

        IReadOnlyList<TCommand> regular = [];
        if (includeRegular)
        {
            var regularCommands = new List<TCommand>(eligibleCommands.Count);
            foreach (var (command, key) in eligibleCommands)
            {
                if (!featuredCommandKeys.Contains(key))
                {
                    regularCommands.Add(command);
                }
            }

            regular = regularCommands;
        }

        return new Sections<TCommand>(pinned, recent, regular);
    }
}
