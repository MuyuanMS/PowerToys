// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AliasTestBeta;

internal sealed partial class BetaCommandPage : ContentPage
{
    public BetaCommandPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Beta Command";
        Name = "Open";
    }

    public override IContent[] GetContent() => [
        new MarkdownContent("# Beta Extension\n\nThis is **Beta Command** from the Alias Test Beta extension.\n\nUse this to test alias conflicts with the Alpha extension."),
    ];
}
