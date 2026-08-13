// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.PowerToys.Settings.UI.Library;
using PowerToys.DSC.Models.KeyboardManager;
using PowerToys.DSC.Models.ResourceObjects;

namespace PowerToys.DSC.Models.FunctionData;

/// <summary>
/// Function data for the Keyboard Manager profile DSC resource. Reads and
/// writes the remapping profile file selected by the module's active
/// configuration and signals the Keyboard Manager engine to reload after a
/// change.
/// </summary>
public sealed class ProfileFunctionData : BaseFunctionData
{
    // Named event the Keyboard Manager engine listens on to reload its
    // configuration; see SettingsEventName in KeyboardManagerConstants.h.
    public const string SettingsEventName = "PowerToys_KeyboardManager_Event_Settings";

    private static readonly SettingsUtils _settingsUtils = SettingsUtils.Default;
    private readonly Func<bool> _isProcessElevated;

    // The stored profile is serialized without null properties to match the
    // shape written by the C++ editor; the engine's JSON reader throws on
    // null-valued properties, which would make it skip the entry.
    private static readonly JsonSerializerOptions _profileSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // This resource replaces the whole profile, so an unrecognized member in
    // the input (e.g. a typo like "key" for "keys") must fail loudly rather
    // than being ignored, which would otherwise clear the user's remappings.
    private static readonly JsonSerializerOptions _inputDeserializerOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Gets the desired state provided as input, if any.
    /// </summary>
    public ProfileResourceObject Input { get; }

    /// <summary>
    /// Gets the current state read from the profile file.
    /// </summary>
    public ProfileResourceObject Output { get; }

    /// <summary>
    /// Gets the warnings collected while reading the current profile.
    /// </summary>
    public IList<string> Warnings { get; } = [];

    public ProfileFunctionData(string? input = null, Func<bool>? isProcessElevated = null)
    {
        _isProcessElevated = isProcessElevated ?? GetIsProcessElevated;
        Output = new();

        if (string.IsNullOrEmpty(input))
        {
            Input = new();
        }
        else
        {
            // A literal `null` document deserializes to a null object; treat it
            // as invalid rather than defaulting to an empty profile, which would
            // otherwise let `set --input null` silently clear every remapping.
            Input = JsonSerializer.Deserialize<ProfileResourceObject>(input, _inputDeserializerOptions)
                ?? throw new JsonException("The input document must not be null.");
        }
    }

    /// <summary>
    /// Validates the input profile.
    /// </summary>
    /// <returns>The list of validation errors; empty when the input is valid.</returns>
    public IList<string> ValidateInput()
    {
        // A `{"profile":null}` payload deserializes to a null model; reject it
        // as a validation error instead of dereferencing it during conversion.
        if (Input.Profile == null)
        {
            return new List<string> { "The 'profile' property must not be null." };
        }

        return KbmProfileConverter.Validate(Input.Profile);
    }

    /// <summary>
    /// Reads the current profile file into the output state. The read is
    /// non-mutating: a missing or unreadable profile is reported as empty (and,
    /// when unreadable, surfaced as a warning) instead of being overwritten
    /// with defaults, which <see cref="SettingsUtils.GetSettingsOrDefault{T}"/>
    /// would do on a missing or corrupt file.
    /// </summary>
    public void GetState()
    {
        var fileName = GetProfileFileName(out var configurationNeedsNormalization);

        // When the active configuration is missing, empty, or unsafe, the engine
        // would not actually load the file we read here, so a matching profile is
        // an illusion. Surface it as a warning (which makes NeedsUpdate() true)
        // so a set operation runs to normalize the value or reject an unsafe one.
        if (configurationNeedsNormalization)
        {
            Warnings.Add("The Keyboard Manager active configuration is missing or invalid; a set operation is required to normalize it.");
        }

        KeyboardManagerProfile profile;

        if (_settingsUtils.SettingsExists(KeyboardManagerSettings.ModuleName, fileName))
        {
            try
            {
                profile = _settingsUtils.GetSettings<KeyboardManagerProfile>(
                    KeyboardManagerSettings.ModuleName, fileName);

                // A profile file that is the JSON literal `null` deserializes
                // without throwing; treat it as unreadable so it is reported as
                // a warning rather than crashing the conversion below.
                if (profile == null)
                {
                    throw new JsonException("The profile file contains a null document.");
                }
            }
            catch (Exception ex)
            {
                // Do not replace a profile we failed to parse; report it and
                // treat the current state as empty in memory only.
                Warnings.Add($"Could not read the current profile '{fileName}': {ex.Message}");
                profile = new KeyboardManagerProfile();
            }
        }
        else
        {
            profile = new KeyboardManagerProfile();
        }

        Output.Profile = KbmProfileConverter.FromProfile(profile, Warnings);
    }

    /// <summary>
    /// Writes the desired profile to the profile file and signals the
    /// Keyboard Manager engine to reload. Failing to signal is not an error;
    /// the profile is loaded on the next PowerToys start.
    /// </summary>
    /// <returns>True when the running engine was signaled; otherwise false.</returns>
    /// <exception cref="IOException">Thrown when the profile file could not be written.</exception>
    public bool SetState()
    {
        if (_isProcessElevated())
        {
            throw new UnauthorizedAccessException("Keyboard Manager profiles must be applied from a non-elevated process.");
        }

        // Normalize and persist the active configuration so the file we write
        // is exactly the one the engine will load, then write the profile.
        var fileName = EnsureActiveConfigurationAndGetFileName();

        var profile = KbmProfileConverter.ToProfile(Input.Profile);
        var profileJson = JsonSerializer.Serialize(profile, _profileSerializerOptions);
        _settingsUtils.SaveSettings(profileJson, KeyboardManagerSettings.ModuleName, fileName);

        // SettingsUtils.SaveSettings swallows IO exceptions and returns void, so
        // verify the write actually landed before reporting the profile as
        // applied or signaling a reload.
        if (!WriteSucceeded(profileJson, fileName))
        {
            throw new IOException($"Failed to write the profile file '{fileName}'.");
        }

        return SignalSettingsChangedEvent();
    }

    private static bool GetIsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Verifies that the profile file on disk matches the content that was
    /// just written, compensating for the exception-swallowing write API.
    /// </summary>
    /// <param name="expectedJson">The JSON that was written.</param>
    /// <param name="fileName">The profile file name.</param>
    /// <returns>True if the file matches the expected content; otherwise false.</returns>
    private static bool WriteSucceeded(string expectedJson, string fileName)
    {
        try
        {
            var path = _settingsUtils.GetSettingsFilePath(KeyboardManagerSettings.ModuleName, fileName);
            return File.Exists(path) && File.ReadAllText(path) == expectedJson;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Tests whether the desired state matches the current state, comparing
    /// the canonical form of both profiles.
    /// </summary>
    /// <returns>True if the states match; otherwise false.</returns>
    public bool TestState()
    {
        var input = JsonSerializer.SerializeToNode(KbmProfileConverter.Canonicalize(Input.Profile));
        var output = JsonSerializer.SerializeToNode(Output.Profile);
        return JsonNode.DeepEquals(input, output);
    }

    /// <summary>
    /// Gets whether the current profile needs to be rewritten to reach the
    /// desired state. This is true when the desired and current profiles
    /// differ, or when the current profile contains malformed entries that
    /// were skipped while reading (surfaced as warnings): rewriting the whole
    /// profile is what removes those undeclared entries, honoring the
    /// replace-whole-profile semantics.
    /// </summary>
    /// <returns>True when a rewrite is required; otherwise false.</returns>
    public bool NeedsUpdate()
    {
        return !TestState() || Warnings.Count > 0;
    }

    /// <summary>
    /// Gets the difference between the desired and the current state.
    /// </summary>
    /// <returns>A JSON array with the differing property names.</returns>
    public JsonArray GetDiffJson()
    {
        var diff = new JsonArray();
        if (NeedsUpdate())
        {
            diff.Add(ProfileResourceObject.ProfileJsonPropertyName);
        }

        return diff;
    }

    /// <summary>
    /// Gets the schema for the profile resource object.
    /// </summary>
    /// <returns>The JSON schema string.</returns>
    public string Schema()
    {
        return GenerateSchema<ProfileResourceObject>();
    }

    /// <summary>
    /// Gets the profile file name selected by the module's active
    /// configuration, e.g. "default.json". The read is non-mutating: an
    /// unreadable settings file falls back to the default profile instead of
    /// being overwritten, which <see cref="SettingsUtils.GetSettingsOrDefault{T}"/>
    /// would do.
    /// </summary>
    /// <returns>The profile file name.</returns>
    private static string GetProfileFileName(out bool needsNormalization)
    {
        string? activeConfiguration = null;
        var settingsReadable = false;

        if (_settingsUtils.SettingsExists(KeyboardManagerSettings.ModuleName))
        {
            try
            {
                var settings = _settingsUtils.GetSettings<KeyboardManagerSettings>(KeyboardManagerSettings.ModuleName);
                activeConfiguration = settings?.Properties?.ActiveConfiguration?.Value;
                settingsReadable = settings != null;
            }
            catch (Exception)
            {
                // Fall back to the default profile rather than overwriting an
                // unreadable settings file.
            }
        }

        // Normalization is needed when the settings are missing/unreadable, the
        // active configuration is empty, or it is not a safe file name. The
        // deserialized model defaults the value to "default", so also treat a
        // raw file that omits the property as needing normalization: the engine
        // reads the raw file and would not load any remaps in that case.
        needsNormalization = !settingsReadable
            || !IsSafeConfigurationName(activeConfiguration)
            || !RawSettingsHasActiveConfiguration();
        return BuildProfileFileName(activeConfiguration);
    }

    /// <summary>
    /// Builds a profile file name from an active configuration value, falling
    /// back to "default" when the value is empty or not a safe file name. This
    /// prevents a user-writable settings value such as <c>..\\..\\target</c>
    /// from escaping the module directory (a privileged arbitrary-write risk
    /// when the resource runs elevated).
    /// </summary>
    /// <param name="activeConfiguration">The stored active configuration.</param>
    /// <returns>The profile file name, e.g. "default.json".</returns>
    private static string BuildProfileFileName(string? activeConfiguration)
    {
        return IsSafeConfigurationName(activeConfiguration)
            ? $"{activeConfiguration}.json"
            : "default.json";
    }

    /// <summary>
    /// Determines whether the raw settings file actually carries an engine-
    /// readable <c>properties.activeConfiguration.value</c> string. The
    /// deserialized <see cref="KeyboardManagerProperties"/> cannot answer this:
    /// its constructor defaults the value to "default", so a file that omits
    /// <c>properties</c> or <c>activeConfiguration</c> still deserializes to a
    /// non-empty value. The C++ engine reads the raw file directly and its
    /// <c>MappingConfiguration::LoadSettings</c> returns before loading any
    /// remaps when the property is absent, so we must inspect the raw JSON to
    /// keep the two paths in agreement.
    /// </summary>
    /// <returns>True when the raw file contains a non-empty active configuration value.</returns>
    private static bool RawSettingsHasActiveConfiguration()
    {
        try
        {
            var path = _settingsUtils.GetSettingsFilePath(KeyboardManagerSettings.ModuleName);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("activeConfiguration", out var activeConfiguration)
                && activeConfiguration.ValueKind == JsonValueKind.Object
                && activeConfiguration.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(value.GetString());
        }
        catch (Exception)
        {
            // An unreadable/corrupt settings file is handled by the callers,
            // which fall back to normalization rather than trusting the model.
            return false;
        }
    }

    /// <summary>
    /// Determines whether an active configuration value is a safe, single-
    /// segment file name (no rooting, path separators, "."/"..", or invalid
    /// file name characters).
    /// </summary>
    /// <param name="name">The candidate configuration name.</param>
    /// <returns>True if the value is safe to use as a file name; otherwise false.</returns>
    private static bool IsSafeConfigurationName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name is "." or "..")
        {
            return false;
        }

        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (Path.IsPathRooted(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Ensures the module settings exist and carry a non-empty active
    /// configuration, persisting the normalized value, and returns the
    /// corresponding profile file name. This keeps the written profile and the
    /// engine's <c>MappingConfiguration::LoadSettings</c> in agreement: the
    /// engine reads <c>activeConfiguration</c> verbatim, so an empty value
    /// would make it open ".json" rather than the file we write here.
    /// </summary>
    /// <returns>The profile file name to write, e.g. "default.json".</returns>
    private static string EnsureActiveConfigurationAndGetFileName()
    {
        KeyboardManagerSettings settings;
        var settingsExist = _settingsUtils.SettingsExists(KeyboardManagerSettings.ModuleName);

        try
        {
            if (settingsExist)
            {
                settings = _settingsUtils.GetSettings<KeyboardManagerSettings>(KeyboardManagerSettings.ModuleName);

                // A settings file that is the JSON literal `null` deserializes
                // to null without throwing; treat it as unreadable so it is
                // reported via the structured DSC error below instead of
                // throwing a NullReferenceException.
                if (settings == null)
                {
                    throw new JsonException("The Keyboard Manager settings file contains a null document.");
                }
            }
            else
            {
                settings = new KeyboardManagerSettings();
            }
        }
        catch (Exception ex)
        {
            // An existing settings file that cannot be read holds editor options
            // and other module metadata; fail the set rather than overwriting it
            // with defaults, which would erase that state.
            throw new IOException("The Keyboard Manager settings file could not be read; aborting to avoid overwriting it.", ex);
        }

        settings.Properties ??= new KeyboardManagerProperties();

        var activeConfiguration = settings.Properties.ActiveConfiguration?.Value;
        var normalized = false;
        if (string.IsNullOrEmpty(activeConfiguration))
        {
            activeConfiguration = "default";
            settings.Properties.ActiveConfiguration = new GenericProperty<string>(activeConfiguration);
            normalized = true;
        }
        else if (!IsSafeConfigurationName(activeConfiguration))
        {
            // Refuse to use an unsafe configuration name as a path segment; this
            // would let a user-writable value redirect the write outside the
            // module directory (a privileged arbitrary-write risk when elevated).
            throw new IOException($"The Keyboard Manager active configuration '{activeConfiguration}' is not a valid profile name.");
        }
        else if (settingsExist && !RawSettingsHasActiveConfiguration())
        {
            // The model defaulted the value to "default" because the raw file
            // omits properties.activeConfiguration. The engine reads the raw
            // file and would return before loading any remaps, so persist the
            // property to keep the write and the engine's load in agreement.
            settings.Properties.ActiveConfiguration = new GenericProperty<string>(activeConfiguration);
            normalized = true;
        }

        // Persist when the settings file is missing (so the engine can resolve
        // the active configuration at all) or when we normalized an empty value
        // (so it loads the same profile we write). SaveSettings swallows IO
        // failures and returns void, so verify the write actually landed.
        if (!settingsExist || normalized)
        {
            var settingsJson = settings.ToJsonString();
            _settingsUtils.SaveSettings(settingsJson, KeyboardManagerSettings.ModuleName);
            if (!SettingsWriteSucceeded(settingsJson))
            {
                throw new IOException("Failed to persist the Keyboard Manager active configuration.");
            }
        }

        return $"{activeConfiguration}.json";
    }

    /// <summary>
    /// Verifies that the module settings file on disk matches the content that
    /// was just written, compensating for the exception-swallowing write API.
    /// </summary>
    /// <param name="expectedJson">The JSON that was written.</param>
    /// <returns>True if the file matches the expected content; otherwise false.</returns>
    private static bool SettingsWriteSucceeded(string expectedJson)
    {
        try
        {
            var path = _settingsUtils.GetSettingsFilePath(KeyboardManagerSettings.ModuleName);
            return File.Exists(path) && File.ReadAllText(path) == expectedJson;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Signals the named event the Keyboard Manager engine listens on so a
    /// running instance reloads the profile immediately. Mirrors the signal
    /// in MappingConfiguration::SaveSettingsToFile.
    /// </summary>
    /// <returns>True if the event was signaled; otherwise false.</returns>
    private static bool SignalSettingsChangedEvent()
    {
        try
        {
            // The engine creates this event while it is running (EventWaiter::
            // start -> CreateEventW). Open the existing event rather than
            // creating it, so that a missing event correctly reports that no
            // running instance received the signal; the caller then surfaces
            // FailedToSignalSettingsEvent ("applied on the next PowerToys
            // start"). Creating the event here would make Set() always succeed
            // and leave that message and this method's false result unused.
            using var settingsEvent = EventWaitHandle.OpenExisting(SettingsEventName);
            return settingsEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No running Keyboard Manager engine has created the event; the new
            // profile will be loaded the next time PowerToys starts.
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
