// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text;

namespace Microsoft.CmdPal.AdaptiveCards.Polyfills;

internal static class ChartFormat
{
    public static CompositeFormat Load(string format, CompositeFormat fallback, int argumentCount)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return fallback;
        }

        try
        {
            var parsed = CompositeFormat.Parse(format);
            if (parsed.MinimumArgumentCount != argumentCount)
            {
                return fallback;
            }

            var arguments = Enumerable.Range(0, argumentCount).Cast<object>().ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, parsed, arguments);
            return parsed;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}
