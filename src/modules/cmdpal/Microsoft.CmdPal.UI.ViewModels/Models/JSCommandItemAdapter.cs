// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Microsoft.CmdPal.UI.ViewModels.Services.JsonRpc;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Microsoft.CmdPal.UI.ViewModels.Models;

/// <summary>
/// Adapts a JSON command item payload to <see cref="ICommandItem"/>.
/// The nested command is resolved lazily so page proxies are created only when needed.
/// </summary>
internal sealed partial class JSCommandItemAdapter : JSObservableProxyBase, ICommandItem
{
    private ICommand? _command;
    private bool _commandResolved;

    public JSCommandItemAdapter(JsonElement data, JsonRpcConnection connection)
        : base(GetNotificationId(data), connection, data)
    {
    }

    public ICommand? Command
    {
        get
        {
            if (!_commandResolved)
            {
                _commandResolved = true;

                var commandData = Data;
                if (JSModelMapper.TryGetAnyCase(Data, "command", "Command", out var commandElement) &&
                    commandElement.ValueKind == JsonValueKind.Object)
                {
                    commandData = commandElement;
                }

                _command = JSCommandFactory.CreateCommandFromJson(commandData, Connection);
            }

            return _command;
        }
    }

    public IContextItem[] MoreCommands => JSModelMapper.ParseContextItems(Data, "moreCommands", "MoreCommands", Connection);

    public IIconInfo Icon => JSModelMapper.TryGetIcon(Data, "icon", "Icon", out var icon)
        ? icon
        : Command?.Icon ?? new IconInfo(string.Empty);

    public string Title => JSModelMapper.GetString(Data, "displayName") ?? JSModelMapper.GetString(Data, "title") ?? string.Empty;

    public string Subtitle => JSModelMapper.GetString(Data, "description") ?? JSModelMapper.GetString(Data, "subtitle") ?? string.Empty;

    protected override bool SupportsProperty(string propertyName) => propertyName switch
    {
        "command" or "moreCommands" or "icon" or "title" or "subtitle" => true,
        _ => false,
    };

    private static string GetNotificationId(JsonElement data)
    {
        if (JSModelMapper.TryGetAnyCase(data, "command", "Command", out var commandElement) &&
            commandElement.ValueKind == JsonValueKind.Object)
        {
            return JSModelMapper.GetString(commandElement, "id") ?? string.Empty;
        }

        return JSModelMapper.GetString(data, "id") ?? string.Empty;
    }
}
