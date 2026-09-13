// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PowerToys.DSC.Models.FancyZones;

/// <summary>
/// A default layout: either a built-in template with its settings, or a
/// reference to a custom layout.
/// </summary>
public sealed class FzDefaultLayout
{
    /// <summary>
    /// Gets or sets the layout type.
    /// </summary>
    [JsonPropertyName("type")]
    [Required]
    [Description("The layout type: blank, focus, rows, columns, grid, priority-grid, or custom.")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier of the custom layout; required when the type is custom.
    /// </summary>
    [JsonPropertyName("uuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("The identifier (GUID) of the custom layout. Required when the type is 'custom', not allowed otherwise.")]
    public string? Uuid { get; set; }

    /// <summary>
    /// Gets or sets the number of zones of a template layout.
    /// </summary>
    [JsonPropertyName("zoneCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("The number of zones of a template layout. Default 0 for blank; 3 for all other template types. Maximum 128. Not allowed for custom layouts.")]
    public int? ZoneCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether space is left between zones.
    /// </summary>
    [JsonPropertyName("showSpacing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("Whether space is left between the zones. Default true for grid templates. Not allowed for custom layouts.")]
    public bool? ShowSpacing { get; set; }

    /// <summary>
    /// Gets or sets the space between zones.
    /// </summary>
    [JsonPropertyName("spacing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("The space between the zones, in pixels. Default 16 for grid templates. Not allowed for custom layouts.")]
    public int? Spacing { get; set; }

    /// <summary>
    /// Gets or sets the distance from a zone edge at which highlighting starts.
    /// </summary>
    [JsonPropertyName("sensitivityRadius")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("The distance, in pixels, from a zone edge at which the zone is highlighted while dragging. Default 20 for template layouts. Not allowed for custom layouts.")]
    public int? SensitivityRadius { get; set; }
}
