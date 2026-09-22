// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.PowerToys.Settings.UI.Library;

public sealed class ScreenTranslatorLanguageOption
{
    public ScreenTranslatorLanguageOption(string code, string displayName, bool isEnabled = true)
    {
        Code = code;
        DisplayName = displayName;
        IsEnabled = isEnabled;
    }

    public string Code { get; }

    public string DisplayName { get; }

    public bool IsEnabled { get; }

    public double Opacity => IsEnabled ? 1.0 : 0.45;
}
