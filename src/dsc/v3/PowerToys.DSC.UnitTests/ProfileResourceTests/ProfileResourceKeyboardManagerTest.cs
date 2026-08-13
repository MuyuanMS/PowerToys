// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerToys.DSC.DSCResources;
using PowerToys.DSC.Models;
using PowerToys.DSC.Models.FunctionData;
using PowerToys.DSC.Models.KeyboardManager;
using PowerToys.DSC.Models.ResourceObjects;
using PowerToys.DSC.UnitTests.Models;

namespace PowerToys.DSC.UnitTests.ProfileResourceTests;

[TestClass]
public sealed class ProfileResourceKeyboardManagerTest : BaseDscTest
{
    private const string DefaultProfileFileName = "default.json";
    private const string WorkProfileFileName = "work.json";

    private static readonly JsonSerializerOptions _profileSerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private MockFileSystem _fileSystem;
    private SettingsUtils _settingsUtils;
    private ProfileResource _resource;
    private bool _settingsEventSignaled;

    private static string Module => nameof(ModuleType.KeyboardManager);

    [TestInitialize]
    public void TestInitialize()
    {
        _fileSystem = new MockFileSystem();
        _settingsUtils = new SettingsUtils(_fileSystem);
        _resource = new ProfileResource(
            Module,
            input => new ProfileFunctionData(
                input,
                () => false,
                _fileSystem,
                () =>
                {
                    _settingsEventSignaled = true;
                    return true;
                }));

        _settingsUtils.SaveSettings(new KeyboardManagerSettings().ToJsonString(), KeyboardManagerSettings.ModuleName);
        _settingsUtils.SaveSettings(JsonSerializer.Serialize(new KeyboardManagerProfile()), KeyboardManagerSettings.ModuleName, DefaultProfileFileName);
    }

    [TestMethod]
    public void Get_Success()
    {
        // Arrange
        var profile = CreateSampleProfileModel();
        SaveProfile(KbmProfileConverter.ToProfile(profile), DefaultProfileFileName);

        // Act
        var result = ExecuteProfileResource(resource => resource.GetState(null));
        var state = result.OutputState<ProfileResourceObject>();

        // Assert
        Assert.IsTrue(result.Success);
        AssertProfilesAreEqual(KbmProfileConverter.Canonicalize(profile), state.Profile);
    }

    [TestMethod]
    public void Export_Success()
    {
        // Arrange
        var profile = CreateSampleProfileModel();
        SaveProfile(KbmProfileConverter.ToProfile(profile), DefaultProfileFileName);

        // Act
        var result = ExecuteProfileResource(resource => resource.ExportState(null));
        var state = result.OutputState<ProfileResourceObject>();

        // Assert
        Assert.IsTrue(result.Success);
        AssertProfilesAreEqual(KbmProfileConverter.Canonicalize(profile), state.Profile);
    }

    [TestMethod]
    public void Export_SkippedMapping_WritesWarning()
    {
        // Arrange
        var profile = new KeyboardManagerProfile();
        profile.RemapShortcuts.AppSpecificRemapShortcuts.Add(new AppSpecificKeysDataModel
        {
            OriginalKeys = "17;65",
            NewRemapKeys = "17;86",
            TargetApp = " notepad.exe ",
        });
        SaveProfile(profile, DefaultProfileFileName);

        // Act
        var result = ExecuteProfileResource(resource => resource.ExportState(null));
        var messages = result.Messages();

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, messages.Count);
        Assert.AreEqual(DscMessageLevel.Warning, messages[0].Level);
        StringAssert.Contains(messages[0].Message, "surrounding whitespace");
    }

    [TestMethod]
    public void SetWithDiff_Success()
    {
        // Arrange
        var input = CreateInputResourceObject(CreateSampleProfileModel());

        // Act
        var result = ExecuteProfileResource(resource => resource.SetState(input));
        var (state, diff) = result.OutputStateAndDiff<ProfileResourceObject>();

        // Assert
        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new List<string> { ProfileResourceObject.ProfileJsonPropertyName }, diff);
        AssertProfilesAreEqual(KbmProfileConverter.Canonicalize(CreateSampleProfileModel()), state.Profile);

        // The stored profile uses the exact editor encoding
        var stored = GetProfile(DefaultProfileFileName);
        Assert.AreEqual("20", stored.RemapKeys.InProcessRemapKeys[0].OriginalKeys);
        Assert.AreEqual("27", stored.RemapKeys.InProcessRemapKeys[0].NewRemapKeys);
        Assert.AreEqual("17;16;65", stored.RemapShortcuts.GlobalRemapShortcuts[0].OriginalKeys);
        Assert.AreEqual("17;86", stored.RemapShortcuts.GlobalRemapShortcuts[0].NewRemapKeys);
    }

    [TestMethod]
    public void SetTwice_SecondSetHasNoDiff()
    {
        // Arrange
        var input = CreateInputResourceObject(CreateSampleProfileModel());

        // Act
        var firstResult = ExecuteProfileResource(resource => resource.SetState(input));
        var secondResult = ExecuteProfileResource(resource => resource.SetState(input));
        var (_, firstDiff) = firstResult.OutputStateAndDiff<ProfileResourceObject>();
        var (_, secondDiff) = secondResult.OutputStateAndDiff<ProfileResourceObject>();

        // Assert
        Assert.IsTrue(firstResult.Success);
        Assert.IsTrue(secondResult.Success);
        CollectionAssert.AreEqual(new List<string> { ProfileResourceObject.ProfileJsonPropertyName }, firstDiff);
        CollectionAssert.AreEqual(new List<string>(), secondDiff);
    }

    [TestMethod]
    public void TestWithDiff_Success()
    {
        // Arrange
        var input = CreateInputResourceObject(CreateSampleProfileModel());

        // Act
        var result = ExecuteProfileResource(resource => resource.TestState(input));
        var (state, diff) = result.OutputStateAndDiff<ProfileResourceObject>();

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsFalse(state.InDesiredState);
        CollectionAssert.AreEqual(new List<string> { ProfileResourceObject.ProfileJsonPropertyName }, diff);

        // Test must not modify the profile
        Assert.AreEqual(0, GetProfile(DefaultProfileFileName).RemapKeys.InProcessRemapKeys.Count);
    }

    [TestMethod]
    public void TestWithoutDiff_Success()
    {
        // Arrange
        var profile = CreateSampleProfileModel();
        SaveProfile(KbmProfileConverter.ToProfile(profile), DefaultProfileFileName);
        var input = CreateInputResourceObject(profile);

        // Act
        var result = ExecuteProfileResource(resource => resource.TestState(input));
        var (state, diff) = result.OutputStateAndDiff<ProfileResourceObject>();

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsTrue(state.InDesiredState);
        CollectionAssert.AreEqual(new List<string>(), diff);
    }

    [TestMethod]
    public void Set_InvalidProfile_FailsAndLeavesFileUntouched()
    {
        // Arrange
        var input = /*lang=json,strict*/ """{"profile":{"keys":[{"from":"InvalidKeyName","to":"Esc"}]}}""";

        // Act
        var result = ExecuteProfileResource(resource => resource.SetState(input));
        var messages = result.Messages();

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, messages.Count);
        Assert.AreEqual(DscMessageLevel.Error, messages[0].Level);
        StringAssert.Contains(messages[0].Message, "Invalid key name 'InvalidKeyName'");
        Assert.AreEqual(0, GetProfile(DefaultProfileFileName).RemapKeys.InProcessRemapKeys.Count);
    }

    [TestMethod]
    public void Set_RespectsActiveConfiguration()
    {
        // Arrange
        var settings = new KeyboardManagerSettings();
        settings.Properties.ActiveConfiguration.Value = Path.GetFileNameWithoutExtension(WorkProfileFileName);
        _settingsUtils.SaveSettings(settings.ToJsonString(), KeyboardManagerSettings.ModuleName);
        var input = CreateInputResourceObject(CreateSampleProfileModel());

        // Act
        var result = ExecuteProfileResource(resource => resource.SetState(input));

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsTrue(_settingsUtils.SettingsExists(KeyboardManagerSettings.ModuleName, WorkProfileFileName));
        Assert.AreEqual("20", GetProfile(WorkProfileFileName).RemapKeys.InProcessRemapKeys[0].OriginalKeys);
        Assert.AreEqual(0, GetProfile(DefaultProfileFileName).RemapKeys.InProcessRemapKeys.Count);
    }

    [TestMethod]
    public void Set_SignalsSettingsChangedEvent()
    {
        // Arrange
        var input = CreateInputResourceObject(CreateSampleProfileModel());

        // Act
        var result = ExecuteProfileResource(resource => resource.SetState(input));

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsTrue(_settingsEventSignaled);
    }

    private DscExecuteResult ExecuteProfileResource(Func<ProfileResource, bool> action)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            return new DscExecuteResult(action(_resource), output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    /// <summary>
    /// Creates the sample friendly profile used by the tests.
    /// </summary>
    private static KbmProfileModel CreateSampleProfileModel()
    {
        return new KbmProfileModel
        {
            Keys = [new() { From = "CapsLock", To = "Esc" }],
            Shortcuts =
            [
                new() { From = "Ctrl+Shift+A", To = "Ctrl+V" },
                new() { From = "Win+O, K", ToText = "chord" },
                new() { From = "Ctrl+Alt+N", To = "Ctrl+S", TargetApp = "notepad.exe", ExactMatch = true },
            ],
        };
    }

    private static string CreateInputResourceObject(KbmProfileModel profile)
    {
        return JsonSerializer.Serialize(new ProfileResourceObject { Profile = profile });
    }

    private KeyboardManagerProfile GetProfile(string fileName)
    {
        return _settingsUtils.GetSettingsOrDefault<KeyboardManagerProfile>(KeyboardManagerSettings.ModuleName, fileName);
    }

    private void SaveProfile(KeyboardManagerProfile profile, string fileName)
    {
        _settingsUtils.SaveSettings(JsonSerializer.Serialize(profile, _profileSerializerOptions), KeyboardManagerSettings.ModuleName, fileName);
    }

    private static void AssertProfilesAreEqual(KbmProfileModel expected, KbmProfileModel actual)
    {
        var expectedJson = JsonSerializer.SerializeToNode(expected);
        var actualJson = JsonSerializer.SerializeToNode(actual);
        Assert.IsTrue(JsonNode.DeepEquals(expectedJson, actualJson), $"{expectedJson} != {actualJson}");
    }
}
