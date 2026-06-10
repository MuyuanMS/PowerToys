// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AliasTestBeta;

public partial class AliasTestBetaCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public AliasTestBetaCommandsProvider()
    {
        DisplayName = "Alias Test Beta";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new CommandItem(new BetaCommandPage()) { Title = "Beta Command" },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;
}
