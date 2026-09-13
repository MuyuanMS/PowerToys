// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.PowerToys.Settings.UI.Library;
using PowerToys.DSC.Models.ResourceObjects;

namespace PowerToys.DSC.Models.FunctionData;

/// <summary>
/// Function data for the ZoomIt module of the settings DSC resource. ZoomIt
/// keeps its settings in the registry (HKCU\Software\Sysinternals\ZoomIt)
/// rather than in a settings.json file, so the state is read and written
/// through the ZoomIt settings interop the Settings app uses, and a running
/// ZoomIt instance is signaled to reload its settings after a change.
/// Properties that are not part of the desired state keep their current
/// value, mirroring how the interop only updates the values it is given.
/// </summary>
public sealed class ZoomItSettingsFunctionData : BaseFunctionData, ISettingsFunctionData
{
    // Named event ZoomIt listens on to reload its settings; see
    // ZOOMIT_REFRESH_SETTINGS_EVENT in shared_constants.h.
    public const string RefreshSettingsEventName = "Local\\PowerToysZoomIt-RefreshSettingsEvent-f053a563-d519-4b0d-8152-a54489c13324";

    private const string PropertiesJsonPropertyName = "properties";

    // Match the options the Settings app uses to read the interop JSON.
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        MaxDepth = 0,
        IncludeFields = true,
    };

    private readonly SettingsResourceObject<ZoomItSettings> _input;
    private readonly SettingsResourceObject<ZoomItSettings> _output;
    private readonly bool _hasInput;
    private readonly bool _recordScalingSpecified;
    private string? _currentRecordFormat;

    /// <summary>
    /// Gets or sets the reader of the ZoomIt settings JSON. Defaults to the
    /// ZoomIt settings interop, which reads the registry; tests replace it.
    /// </summary>
    public static Func<string> LoadSettingsJson { get; set; } = () => global::PowerToys.ZoomItSettingsInterop.ZoomItSettings.LoadSettingsJson();

    /// <summary>
    /// Gets or sets the writer of the ZoomIt settings JSON. Defaults to the
    /// ZoomIt settings interop, which writes the registry; tests replace it.
    /// </summary>
    public static Action<string> SaveSettingsJson { get; set; } = json => global::PowerToys.ZoomItSettingsInterop.ZoomItSettings.SaveSettingsJson(json);

    /// <summary>
    /// Gets or sets the operation that signals ZoomIt to reload its settings.
    /// Tests replace it to avoid interacting with a running ZoomIt instance.
    /// </summary>
    public static Action SignalRefreshSettings { get; set; } = SignalRefreshSettingsEvent;

    /// <inheritdoc/>
    public ISettingsResourceObject Input => _input;

    /// <inheritdoc/>
    public ISettingsResourceObject Output => _output;

    public ZoomItSettingsFunctionData(string? input = null)
    {
        _output = new();
        _hasInput = !string.IsNullOrEmpty(input);
        _recordScalingSpecified = _hasInput &&
            JsonNode.Parse(input!)?[SettingsResourceObject<ZoomItSettings>.SettingsJsonPropertyName]?[PropertiesJsonPropertyName]?["RecordScaling"] != null;
        _input = _hasInput ? JsonSerializer.Deserialize<SettingsResourceObject<ZoomItSettings>>(input!, _serializerOptions) ?? new() : new();
    }

    /// <summary>
    /// Reads the current settings from the registry through the interop.
    /// Properties the desired state does not specify are completed from the
    /// current settings, so that the comparison and the write only cover the
    /// properties the configuration declares.
    /// </summary>
    public void GetState()
    {
        _output.Settings = JsonSerializer.Deserialize<ZoomItSettings>(LoadSettingsJson(), _serializerOptions) ?? new();
        _currentRecordFormat = _output.Settings.Properties.RecordFormat?.Value;
        if (_hasInput)
        {
            _input.Settings = MergeWithCurrent(_input.Settings, _output.Settings);
        }
    }

    /// <summary>
    /// Writes the settings to the registry through the interop and signals a
    /// running ZoomIt instance to reload them. Failing to signal is not an
    /// error; the settings are loaded the next time ZoomIt starts.
    /// </summary>
    public void SetState()
    {
        Debug.Assert(_output.Settings != null, "Output settings should not be null");
        var settings = JsonSerializer.SerializeToNode(_output.Settings, _serializerOptions);
        if (_recordScalingSpecified &&
            !string.Equals(_currentRecordFormat, _output.Settings.Properties.RecordFormat?.Value, StringComparison.Ordinal) &&
            settings?[PropertiesJsonPropertyName] is JsonObject properties &&
            properties["RecordScaling"]?.DeepClone() is JsonNode recordScaling)
        {
            properties.Remove("RecordScaling");
            SaveSettingsJson(settings.ToJsonString(_serializerOptions));
            properties["RecordScaling"] = recordScaling;
        }

        SaveSettingsJson(settings?.ToJsonString(_serializerOptions) ?? JsonSerializer.Serialize(_output.Settings, _serializerOptions));
        SignalRefreshSettings();
    }

    /// <inheritdoc/>
    public bool TestState()
    {
        var input = JsonSerializer.SerializeToNode(_input.Settings, _serializerOptions);
        var output = JsonSerializer.SerializeToNode(_output.Settings, _serializerOptions);
        RemoveDerivedHotkeyKeys(input);
        RemoveDerivedHotkeyKeys(output);
        return JsonNode.DeepEquals(input, output);
    }

    /// <inheritdoc/>
    public JsonArray GetDiffJson()
    {
        var diff = new JsonArray();
        if (!TestState())
        {
            diff.Add(SettingsResourceObject<ZoomItSettings>.SettingsJsonPropertyName);
        }

        return diff;
    }

    /// <inheritdoc/>
    public string Schema()
    {
        return GenerateSchema<SettingsResourceObject<ZoomItSettings>>();
    }

    /// <summary>
    /// Completes the desired settings with the current value of every
    /// property the desired settings do not specify.
    /// </summary>
    private static ZoomItSettings MergeWithCurrent(ZoomItSettings desired, ZoomItSettings current)
    {
        if (JsonSerializer.SerializeToNode(desired, _serializerOptions) is not JsonObject desiredNode ||
            JsonSerializer.SerializeToNode(current, _serializerOptions)?[PropertiesJsonPropertyName] is not JsonObject currentProperties)
        {
            return desired;
        }

        if (desiredNode[PropertiesJsonPropertyName] is not JsonObject desiredProperties)
        {
            desiredProperties = new JsonObject();
            desiredNode[PropertiesJsonPropertyName] = desiredProperties;
        }

        foreach (var (name, value) in currentProperties)
        {
            if (desiredProperties[name] == null && value != null)
            {
                desiredProperties[name] = value.DeepClone();
            }
        }

        return desiredNode.Deserialize<ZoomItSettings>(_serializerOptions) ?? desired;
    }

    private static void RemoveDerivedHotkeyKeys(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            if (jsonObject.ContainsKey("win") &&
                jsonObject.ContainsKey("ctrl") &&
                jsonObject.ContainsKey("alt") &&
                jsonObject.ContainsKey("shift") &&
                jsonObject.ContainsKey("code"))
            {
                jsonObject.Remove("key");
            }

            foreach (var (_, value) in jsonObject)
            {
                RemoveDerivedHotkeyKeys(value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var value in jsonArray)
            {
                RemoveDerivedHotkeyKeys(value);
            }
        }
    }

    /// <summary>
    /// Signals the named event ZoomIt listens on so a running instance
    /// reloads its settings immediately, as the Settings app does through
    /// the runner. The event is only opened, never created: when no ZoomIt
    /// instance is running there is nothing to signal.
    /// </summary>
    private static void SignalRefreshSettingsEvent()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(RefreshSettingsEventName, out var refreshEvent))
            {
                using (refreshEvent)
                {
                    refreshEvent.Set();
                }
            }
        }
        catch (Exception)
        {
            // Best effort; the settings take effect the next time ZoomIt starts.
        }
    }
}
