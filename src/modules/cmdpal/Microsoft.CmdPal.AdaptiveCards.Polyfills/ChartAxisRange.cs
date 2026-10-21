// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.AdaptiveCards.Polyfills;

/// <summary>The value range of a chart axis and the spacing of its grid lines.</summary>
internal readonly record struct ChartAxisRange(double Min, double Max, double Step)
{
    private const int MaxTickCount = 64;

    /// <summary>Maps <paramref name="value"/> to 0 (minimum) through 1 (maximum), clamped.</summary>
    public double Normalize(double value) =>
        Max > Min ? Math.Clamp((value - Min) / (Max - Min), 0, 1) : 0;

    /// <summary>Returns up to 64 grid line values within the axis range.</summary>
    /// <remarks>The limit keeps pathological ranges from creating excessive visual geometry.</remarks>
    public IReadOnlyList<double> GetTicks()
    {
        var ticks = new List<double>();
        if (Step <= 0 || Max <= Min)
        {
            ticks.Add(Min);
            return ticks;
        }

        var first = Math.Ceiling((Min / Step) - 1e-9);
        var last = Math.Floor((Max / Step) + 1e-9);
        var firstTick = first * Step;
        for (var offset = 0; offset < MaxTickCount && first + offset <= last; offset++)
        {
            var tick = Math.Round(firstTick + (offset * Step), 10);
            if (ticks.Count == 0 || tick != ticks[^1])
            {
                ticks.Add(tick);
            }
        }

        return ticks;
    }
}
