// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerToys.DSC.Commands;
using PowerToys.DSC.DSCResources;
using PowerToys.DSC.Models.FunctionData;
using PowerToys.DSC.Models.ResourceObjects;

namespace PowerToys.DSC.UnitTests.SettingsResourceTests;

/// <summary>
/// ZoomIt stores its settings in the registry, read and written through the
/// ZoomIt settings interop. Behavior tests replace the interop with an
/// in-memory store; a separate read-only smoke test exercises the real loader.
/// </summary>
[TestClass]
public sealed class SettingsResourceZoomItModuleTest : BaseDscTest
{
    // The shape the interop produces: every property wrapped in a "value" object
    private const string InteropSettingsJson = /*lang=json,strict*/ """
        {
          "name": "ZoomIt",
          "version": "1.0",
          "properties": {
            "ToggleKey": { "value": { "win": false, "ctrl": true, "alt": false, "shift": false, "code": 49, "key": "1" } },
            "DrawToggleKey": { "value": { "win": false, "ctrl": true, "alt": false, "shift": false, "code": 50, "key": "2" } },
            "BreakTimeout": { "value": 10 },
            "ShowTrayIcon": { "value": true },
            "RecordFormat": { "value": "GIF" },
            "RecordScaling": { "value": 100 },
            "Font": { "value": "AAAAAAAAAAAAAAAAAAAAAA==" }
          }
        }
        """;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        MaxDepth = 0,
        IncludeFields = true,
    };

    private static readonly JsonSerializerOptions _inputSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private Func<string> _originalLoadSettingsJson;
    private Action<string> _originalSaveSettingsJson;
    private string _originalRefreshSettingsEventName;
    private string _store;
    private List<string> _saved;

    private static string Module => nameof(ModuleType.ZoomIt);

    [TestInitialize]
    public void TestInitialize()
    {
        _originalLoadSettingsJson = ZoomItSettingsFunctionData.LoadSettingsJson;
        _originalSaveSettingsJson = ZoomItSettingsFunctionData.SaveSettingsJson;
        _originalRefreshSettingsEventName = ZoomItSettingsFunctionData.RefreshSettingsEventName;
        _store = InteropSettingsJson;
        _saved = [];
        ZoomItSettingsFunctionData.LoadSettingsJson = () => _store;
        ZoomItSettingsFunctionData.SaveSettingsJson = json =>
        {
            _saved.Add(json);
            _store = json;
        };

        ZoomItSettingsFunctionData.RefreshSettingsEventName = $"Local\\PowerToysDscTest-ZoomItRefreshSettingsEvent-{Guid.NewGuid()}";
    }

    [TestCleanup]
    public void TestCleanup()
    {
        ZoomItSettingsFunctionData.LoadSettingsJson = _originalLoadSettingsJson;
        ZoomItSettingsFunctionData.SaveSettingsJson = _originalSaveSettingsJson;
        ZoomItSettingsFunctionData.RefreshSettingsEventName = _originalRefreshSettingsEventName;
    }

    [TestMethod]
    public void Get_ReturnsRegistryBackedSettings()
    {
        // Act
        var result = ExecuteDscCommand<GetCommand>("--resource", SettingsResource.ResourceName, "--module", Module);
        var state = result.OutputState<SettingsResourceObject<ZoomItSettings>>();

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(49, state.Settings.Properties.ToggleKey.Value.Code);
        Assert.IsTrue(state.Settings.Properties.ToggleKey.Value.Ctrl);
        Assert.AreEqual(10, state.Settings.Properties.BreakTimeout.Value);
        Assert.IsTrue(state.Settings.Properties.ShowTrayIcon.Value);
        Assert.AreEqual("GIF", state.Settings.Properties.RecordFormat.Value);
        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public void Export_Success()
    {
        // Act
        var result = ExecuteDscCommand<ExportCommand>("--resource", SettingsResource.ResourceName, "--module", Module);
        var state = result.OutputState<SettingsResourceObject<ZoomItSettings>>();

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(10, state.Settings.Properties.BreakTimeout.Value);
        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public void SetWithDiff_WritesDeclaredPropertiesAndKeepsTheOthers()
    {
        // Arrange
        var input = CreateInput(properties =>
        {
            properties.BreakTimeout = new IntProperty(25);
            properties.ShowTrayIcon = new BoolProperty(false);
        });

        // Act
        var result = ExecuteDscCommand<SetCommand>("--resource", SettingsResource.ResourceName, "--module", Module, "--input", input);
        var (state, diff) = result.OutputStateAndDiff<SettingsResourceObject<ZoomItSettings>>();

        // Assert
        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new List<string> { SettingsResourceObject<ZoomItSettings>.SettingsJsonPropertyName }, diff);
        Assert.AreEqual(25, state.Settings.Properties.BreakTimeout.Value);
        Assert.IsFalse(state.Settings.Properties.ShowTrayIcon.Value);

        // Properties the configuration does not declare keep their current value
        Assert.AreEqual(49, state.Settings.Properties.ToggleKey.Value.Code);
        Assert.AreEqual("GIF", state.Settings.Properties.RecordFormat.Value);

        // The interop receives the complete settings in its own shape
        Assert.AreEqual(1, _saved.Count);
        var saved = JsonNode.Parse(_saved[0]);
        Assert.AreEqual("ZoomIt", saved["name"].GetValue<string>());
        Assert.AreEqual(25, saved["properties"]["BreakTimeout"]["value"].GetValue<int>());
        Assert.IsFalse(saved["properties"]["ShowTrayIcon"]["value"].GetValue<bool>());
        Assert.AreEqual(49, saved["properties"]["ToggleKey"]["value"]["code"].GetValue<int>());
        Assert.AreEqual("AAAAAAAAAAAAAAAAAAAAAA==", saved["properties"]["Font"]["value"].GetValue<string>());
    }

    [TestMethod]
    public void SetTwice_SecondSetHasNoDiffAndDoesNotWrite()
    {
        // Arrange
        var input = CreateInput(properties => properties.BreakTimeout = new IntProperty(25));

        // Act
        var firstResult = ExecuteDscCommand<SetCommand>("--resource", SettingsResource.ResourceName, "--module", Module, "--input", input);
        var secondResult = ExecuteDscCommand<SetCommand>("--resource", SettingsResource.ResourceName, "--module", Module, "--input", input);
        var (_, firstDiff) = firstResult.OutputStateAndDiff<SettingsResourceObject<ZoomItSettings>>();
        var (_, secondDiff) = secondResult.OutputStateAndDiff<SettingsResourceObject<ZoomItSettings>>();

        // Assert
        Assert.IsTrue(firstResult.Success);
        Assert.IsTrue(secondResult.Success);
        CollectionAssert.AreEqual(new List<string> { SettingsResourceObject<ZoomItSettings>.SettingsJsonPropertyName }, firstDiff);
        CollectionAssert.AreEqual(new List<string>(), secondDiff);
        Assert.AreEqual(1, _saved.Count);
    }

    [TestMethod]
    public void SetWithFormatAndScaling_StagesTheScaleAfterTheFormatChange()
    {
        // Arrange
        var input = CreateInput(properties =>
        {
            properties.RecordFormat = new StringProperty("MP4");
            properties.RecordScaling = new IntProperty(50);
        });

        // Act
        var result = ExecuteDscCommand<SetCommand>("--resource", SettingsResource.ResourceName, "--module", Module, "--input", input);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, _saved.Count);
        Assert.IsNull(JsonNode.Parse(_saved[0])["properties"]["RecordScaling"]);
        Assert.AreEqual("MP4", JsonNode.Parse(_saved[0])["properties"]["RecordFormat"]["value"].GetValue<string>());
        Assert.AreEqual(50, JsonNode.Parse(_saved[1])["properties"]["RecordScaling"]["value"].GetValue<int>());
    }

    [TestMethod]
    public void SetWithoutDiff_DoesNotWrite()
    {
        // Arrange: the desired value already matches the current one
        var input = CreateInput(properties => properties.BreakTimeout = new IntProperty(10));

        // Act
        var result = ExecuteDscCommand<SetCommand>("--resource", SettingsResource.ResourceName, "--module", Module, "--input", input);
        var (_, diff) = result.OutputStateAndDiff<SettingsResourceObject<ZoomItSettings>>();

        // Assert
        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new List<string>(), diff);
        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public void SetWithNegativeNumericValue_RejectsBeforeWriting()
    {
        // Arrange
        var input = CreateInput(properties => properties.BreakTimeout = new IntProperty(-1));
        var data = new ZoomItSettingsFunctionData(input);
        data.GetState();
        data.Output.SettingsInternal = data.Input.SettingsInternal;

        // Act and assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(data.SetState);
        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public void SetWithOutOfRangeZoominSliderLevel_RejectsBeforeWriting()
    {
        // Arrange
        var inputNode = JsonNode.Parse(CreateInput(properties => properties.BreakTimeout = new IntProperty(25)));
        inputNode!["settings"]!["properties"]!["ZoominSliderLevel"] = new JsonObject { ["value"] = 6 };
        var data = new ZoomItSettingsFunctionData(inputNode.ToJsonString());
        data.GetState();
        data.Output.SettingsInternal = data.Input.SettingsInternal;

        // Act and assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(data.SetState);
        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public void SetWithOutOfRangeNumericSettings_RejectsBeforeWriting()
    {
        var invalidValues = new (string Name, int Value)[]
        {
            ("BreakOpacity", 0),
            ("BreakOpacity", 101),
            ("BreakTimerPosition", 9),
            ("DemoTypeSpeedSlider", 9),
            ("DemoTypeSpeedSlider", 101),
            ("BreakTimeout", 0),
            ("BreakTimeout", 100),
            ("RecordScaling", 0),
            ("RecordScaling", 101),
            ("RecordScaling", 55),
            ("WebcamPosition", 4),
            ("WebcamSize", 5),
            ("WebcamShape", 4),
            ("WebcamBackgroundMode", 3),
            ("WebcamBrightness", 101),
        };

        foreach (var (name, value) in invalidValues)
        {
            var data = new ZoomItSettingsFunctionData(CreateInputWithIntegerProperty(name, value));
            data.GetState();
            data.Output.SettingsInternal = data.Input.SettingsInternal;

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(data.SetState, $"{name}={value} should be rejected.");
        }

        var invalidHotkey = CreateInput(properties =>
            properties.ToggleKey = new KeyboardKeysProperty(new HotkeySettings(false, false, false, false, 256)));
        var hotkeyData = new ZoomItSettingsFunctionData(invalidHotkey);
        hotkeyData.GetState();
        hotkeyData.Output.SettingsInternal = hotkeyData.Input.SettingsInternal;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(hotkeyData.SetState);

        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public void SetWithNumericSettingBoundaries_AcceptsSupportedValues()
    {
        var inputs = new[]
        {
            CreateInput(properties =>
            {
                properties.BreakTimeout = new IntProperty(1);
                properties.BreakOpacity = new IntProperty(1);
                properties.BreakTimerPosition = new IntProperty(0);
                properties.DemoTypeSpeedSlider = new IntProperty(10);
                properties.ZoominSliderLevel = new IntProperty(0);
                properties.RecordScaling = new IntProperty(10);
                properties.WebcamPosition = new IntProperty(0);
                properties.WebcamSize = new IntProperty(0);
                properties.WebcamShape = new IntProperty(0);
                properties.WebcamBackgroundMode = new IntProperty(0);
                properties.WebcamBrightness = new IntProperty(0);
            }),
            CreateInput(properties =>
            {
                properties.BreakTimeout = new IntProperty(99);
                properties.BreakOpacity = new IntProperty(100);
                properties.BreakTimerPosition = new IntProperty(8);
                properties.DemoTypeSpeedSlider = new IntProperty(100);
                properties.ZoominSliderLevel = new IntProperty(5);
                properties.RecordScaling = new IntProperty(100);
                properties.WebcamPosition = new IntProperty(3);
                properties.WebcamSize = new IntProperty(4);
                properties.WebcamShape = new IntProperty(3);
                properties.WebcamBackgroundMode = new IntProperty(2);
                properties.WebcamBrightness = new IntProperty(100);
            }),
        };

        foreach (var input in inputs)
        {
            var data = new ZoomItSettingsFunctionData(input);
            data.GetState();
            data.Output.SettingsInternal = data.Input.SettingsInternal;
            data.SetState();
        }

        Assert.AreEqual(2, _saved.Count);
    }

    [TestMethod]
    public void SetWithUnsupportedRecordFormat_RejectsBeforeWriting()
    {
        // Arrange
        var input = CreateInput(properties => properties.RecordFormat = new StringProperty("AVI"));
        var data = new ZoomItSettingsFunctionData(input);
        data.GetState();
        data.Output.SettingsInternal = data.Input.SettingsInternal;

        // Act and assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(data.SetState);
        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public void TestWithDiff_Success()
    {
        // Arrange
        var input = CreateInput(properties => properties.ToggleKey = new KeyboardKeysProperty(new HotkeySettings(true, false, false, true, 90)));

        // Act
        var result = ExecuteDscCommand<TestCommand>("--resource", SettingsResource.ResourceName, "--module", Module, "--input", input);
        var (state, diff) = result.OutputStateAndDiff<SettingsResourceObject<ZoomItSettings>>();

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsFalse(state.InDesiredState);
        CollectionAssert.AreEqual(new List<string> { SettingsResourceObject<ZoomItSettings>.SettingsJsonPropertyName }, diff);
        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public void TestWithoutDiff_Success()
    {
        // Arrange
        var input = CreateInput(properties => properties.ToggleKey = new KeyboardKeysProperty(new HotkeySettings(false, true, false, false, 49)));

        // Act
        var result = ExecuteDscCommand<TestCommand>("--resource", SettingsResource.ResourceName, "--module", Module, "--input", input);
        var (state, diff) = result.OutputStateAndDiff<SettingsResourceObject<ZoomItSettings>>();

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsTrue(state.InDesiredState);
        CollectionAssert.AreEqual(new List<string>(), diff);
    }

    [TestMethod]
    public void Set_SignalsRefreshSettingsEvent()
    {
        // Arrange: stand in for a running ZoomIt instance
        using var refreshEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ZoomItSettingsFunctionData.RefreshSettingsEventName);
        var input = CreateInput(properties => properties.BreakTimeout = new IntProperty(25));

        // Act
        var result = ExecuteDscCommand<SetCommand>("--resource", SettingsResource.ResourceName, "--module", Module, "--input", input);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsTrue(refreshEvent.WaitOne(TimeSpan.FromSeconds(5)), "The ZoomIt refresh settings event was not signaled");
    }

    [TestMethod]
    public void Interop_LoadSettingsJson_DeserializesIntoSettingsModel()
    {
        // Act: read the real registry-backed settings through the interop (read-only)
        var json = _originalLoadSettingsJson();
        var settings = JsonSerializer.Deserialize<ZoomItSettings>(json, _serializerOptions);

        // Assert
        Assert.IsNotNull(settings);
        Assert.AreEqual(ZoomItSettings.ModuleName, settings.Name);
        Assert.IsNotNull(settings.Properties.ToggleKey);
        Assert.IsNotNull(settings.Properties.ToggleKey.Value);
        Assert.IsNotNull(settings.Properties.BreakTimeout);
        Assert.IsNotNull(settings.Properties.RecordFormat);
    }

    [TestMethod]
    public void Interop_SaveSettingsJson_RejectsOversizedNativeValues()
    {
        var oversizedStringJson = JsonSerializer.Serialize(new
        {
            name = "ZoomIt",
            version = "1.0",
            properties = new { DemoTypeFile = new { value = new string('x', 260) } },
        });
        var invalidBinaryJson = JsonSerializer.Serialize(new
        {
            name = "ZoomIt",
            version = "1.0",
            properties = new { Font = new { value = "AA==" } },
        });

        Assert.ThrowsException<ArgumentException>(
            () => global::PowerToys.ZoomItSettingsInterop.ZoomItSettings.SaveSettingsJson(oversizedStringJson));
        Assert.ThrowsException<ArgumentException>(
            () => global::PowerToys.ZoomItSettingsInterop.ZoomItSettings.SaveSettingsJson(invalidBinaryJson));
    }

    private static string CreateInput(Action<ZoomItProperties> configure)
    {
        var settings = new ZoomItSettings();
        configure(settings.Properties);
        return JsonSerializer.Serialize(new SettingsResourceObject<ZoomItSettings> { Settings = settings }, _inputSerializerOptions);
    }

    private static string CreateInputWithIntegerProperty(string propertyName, int value)
    {
        var inputNode = JsonNode.Parse(CreateInput(_ => { }));
        inputNode!["settings"]!["properties"]![propertyName] = new JsonObject { ["value"] = value };
        return inputNode.ToJsonString();
    }
}
