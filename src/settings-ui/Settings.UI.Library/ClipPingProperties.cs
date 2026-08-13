// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.PowerToys.Settings.UI.Library.Enumerations;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class ClipPingProperties
    {
        public const string DefaultOverlayColor = "#FF0000";

        public ClipPingProperties()
        {
            OverlayColor = new StringProperty(DefaultOverlayColor);
        }

        public StringProperty OverlayColor { get; set; }

        public ClipPingOverlay OverlayType { get; set; }

        public static string NormalizeOverlayColor(string value)
        {
            if (value is { Length: 7 } &&
                value[0] == '#' &&
                byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) &&
                byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) &&
                byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                return value;
            }

            return DefaultOverlayColor;
        }

        public string ToJsonString() => JsonSerializer.Serialize(this);
    }
}
