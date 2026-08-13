// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;

namespace ClipPing.FuzzTests;

public class FuzzTests
{
    public static void FuzzSettings(ReadOnlySpan<byte> input)
    {
        string json = Encoding.UTF8.GetString(input);

        try
        {
            var settings = JsonSerializer.Deserialize(
                json,
                SettingsSerializationContext.Default.ClipPingSettings);

            _ = ClipPingSettings.Normalize(settings);
        }
        catch (JsonException)
        {
            // Malformed JSON is an expected input; all other exceptions indicate a bug.
        }
    }
}
