// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Microsoft.CmdPal.JsonRpc.Models;

internal interface IJSPropertyChangeTarget
{
    string TargetKind { get; }

    void ApplyPropertyChanges(JsonElement properties);
}

internal static class JSPropertyChangeTargetKinds
{
    internal const string Command = "command";
    internal const string CommandItem = "commandItem";
    internal const string ListItem = "listItem";
}
