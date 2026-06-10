// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AliasTestAlpha;

internal sealed partial class AlphaCommandPage : ContentPage
{
    public AlphaCommandPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Alpha Command";
        Name = "Open";
    }

    public override IContent[] GetContent() => [
        new MarkdownContent("# Alpha Extension\n\nThis is **Alpha Command** from the Alias Test Alpha extension.\n\nUse this to test alias conflicts with the Beta extension."),
    ];
}
