// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Microsoft.CmdPal.AdaptiveCards.Polyfills;

internal enum GaugeValueFormat
{
    Percentage,
    Fraction,
}

/// <summary>The parsed form of an Adaptive Cards <c>Chart.Gauge</c> element, with the schema's defaults.</summary>
internal sealed class GaugeChartModel : IAdaptiveVisualModel
{
    public string? Title { get; init; }

    public bool ShowTitle { get; init; }

    public string? SubLabel { get; init; }

    public double Value { get; init; }

    public double Min { get; init; }

    public double Max { get; init; } = 100;

    public GaugeValueFormat ValueFormat { get; init; }

    public bool ShowLegend { get; init; } = true;

    public bool ShowMinMax { get; init; } = true;

    public bool ShowNeedle { get; init; } = true;

    public string? ColorSet { get; init; }

    public IReadOnlyList<ChartDataPoint> Segments { get; init; } = [];

    public string IncrementalState { get; init; } = "{}";

    /// <summary>Gets the value's position between the minimum (0) and the maximum (1).</summary>
    public double Fraction => Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0, 1) : 0;

    public string FormatValue(IFormatProvider culture) =>
        ValueFormat == GaugeValueFormat.Fraction
            ? string.Format(culture, "{0:0.##}/{1:0.##}", Value, Max)
            : ChartValueFormatter.FormatPercent(Fraction, culture);

    /// <summary>Returns the index of the segment that contains the value, or -1.</summary>
    public int GetActiveSegment()
    {
        if (!(Max > Min))
        {
            return -1;
        }

        var value = Math.Clamp(Value, Min, Max);
        var start = Min;
        for (var i = 0; i < Segments.Count; i++)
        {
            var end = start + Math.Max(0, Segments[i].Value);
            if (end > start && value >= start && (value < end || (i == Segments.Count - 1 && value == end)))
            {
                return i;
            }

            start = end;
        }

        return -1;
    }

    /// <summary>Returns the normalized share of each segment, or zero shares for an invalid range.</summary>
    public IReadOnlyList<double> GetSegmentShares()
    {
        var shares = new double[Segments.Count];
        var range = Max - Min;
        if (!(range > 0) || !double.IsFinite(range))
        {
            return shares;
        }

        for (var i = 0; i < shares.Length; i++)
        {
            shares[i] = Math.Max(0, Segments[i].Value) / range;
        }

        return shares;
    }

    public static GaugeChartModel Parse(string elementJson, ICollection<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(elementJson);
            var element = document.RootElement;

            var segments = ChartJson.GetDataPoints(element, "segments", "legend", "size", warnings);
            var min = ChartJson.GetNumber(element, "min") ?? 0;
            var segmentTotal = 0d;
            foreach (var segment in segments)
            {
                segmentTotal += Math.Max(0, segment.Value);
            }

            var specifiedMax = ChartJson.GetNumber(element, "max");
            var max = specifiedMax ?? (segmentTotal > 0 ? min + segmentTotal : 100);
            if (max <= min)
            {
                if (specifiedMax.HasValue)
                {
                    warnings.Add("max must be greater than min.");
                }

                max = min + 100;
            }

            return new GaugeChartModel
            {
                Title = ChartJson.GetString(element, "title"),
                ShowTitle = ChartJson.GetBoolean(element, "showTitle") ?? false,
                SubLabel = ChartJson.GetString(element, "subLabel"),
                Value = ChartJson.GetNumber(element, "value") ?? 0,
                Min = min,
                Max = max,
                ValueFormat = ChartJson.GetEnum(element, "valueFormat", GaugeValueFormat.Percentage, warnings),
                ShowLegend = ChartJson.GetBoolean(element, "showLegend") ?? true,
                ShowMinMax = ChartJson.GetBoolean(element, "showMinMax") ?? true,
                ShowNeedle = ChartJson.GetBoolean(element, "showNeedle") ?? true,
                ColorSet = ChartJson.GetString(element, "colorSet"),
                Segments = segments,
                IncrementalState = ChartJson.Canonicalize(element),
            };
        }
        catch (JsonException ex)
        {
            warnings.Add($"The element JSON could not be read: {ex.Message}");
            return new GaugeChartModel();
        }
    }
}
