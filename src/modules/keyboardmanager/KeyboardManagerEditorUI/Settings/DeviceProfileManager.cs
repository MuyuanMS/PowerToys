// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCommon;

namespace KeyboardManagerEditorUI.Settings
{
    /// <summary>
    /// Reads and writes deviceProfiles.json — the device→profile map and the auto-switch enable
    /// flag consumed by the engine for per-keyboard profile auto-switching. Saving signals the
    /// engine (via <see cref="ProfileManager.SignalEngineReload"/>) so it reloads the map.
    /// </summary>
    internal static class DeviceProfileManager
    {
        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "PowerToys",
            "Keyboard Manager",
            "deviceProfiles.json");

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private sealed class DeviceProfilesFile
        {
            public bool AutoSwitchEnabled { get; set; }

            public List<DeviceProfileEntry> Map { get; set; } = new();

            // The engine's profile-cycle hotkey definition. The editor doesn't own this (yet);
            // keep it as a raw node so saving from the editor round-trips it unchanged.
            public System.Text.Json.Nodes.JsonNode? CycleHotkey { get; set; }
        }

        private sealed class DeviceProfileEntry
        {
            public string Device { get; set; } = string.Empty;

            public string Profile { get; set; } = string.Empty;

            // Display name only; the engine ignores this field.
            public string Name { get; set; } = string.Empty;
        }

        public static bool GetAutoSwitchEnabled() => Load().AutoSwitchEnabled;

        /// <summary>Returns the saved keyboard→profile assignments (with display names).</summary>
        public static IReadOnlyList<DeviceAssignment> GetSavedAssignments()
        {
            return Load().Map
                .Where(e => !string.IsNullOrEmpty(e.Device) && !string.IsNullOrEmpty(e.Profile))
                .Select(e => new DeviceAssignment { Device = e.Device, Profile = e.Profile, Name = e.Name })
                .ToList();
        }

        /// <summary>
        /// Persists the auto-switch flag and assignments, then signals the engine to reload.
        /// Assignments with an empty device or profile are dropped (unassigned keyboards).
        /// </summary>
        public static bool Save(bool autoSwitchEnabled, IEnumerable<DeviceAssignment> assignments)
        {
            try
            {
                var file = new DeviceProfilesFile
                {
                    AutoSwitchEnabled = autoSwitchEnabled,
                    Map = assignments
                        .Where(a => !string.IsNullOrEmpty(a.Device) && !string.IsNullOrEmpty(a.Profile))
                        .Select(a => new DeviceProfileEntry { Device = a.Device, Profile = a.Profile, Name = a.Name })
                        .ToList(),
                    CycleHotkey = Load().CycleHotkey, // preserve the engine's hotkey definition
                };

                Write(file);
                ProfileManager.SignalEngineReload();
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to save deviceProfiles.json: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Removes any assignments that point at <paramref name="profile"/>. Returns false only
        /// when the file could not be read or written.
        /// </summary>
        public static bool RemoveAssignmentsForProfile(string profile)
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                return true;
            }

            try
            {
                if (!File.Exists(_filePath))
                {
                    return true;
                }

                DeviceProfilesFile file = JsonSerializer.Deserialize<DeviceProfilesFile>(File.ReadAllText(_filePath), _jsonOptions)
                                          ?? throw new InvalidDataException("deviceProfiles.json does not contain a valid object");
                file.Map = file.Map
                    .Where(entry => !string.Equals(entry.Profile, profile, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Write(file);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to update deviceProfiles.json after deleting a profile: " + ex.Message);
                return false;
            }
        }

        private static DeviceProfilesFile Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    return JsonSerializer.Deserialize<DeviceProfilesFile>(File.ReadAllText(_filePath), _jsonOptions)
                           ?? new DeviceProfilesFile();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to read deviceProfiles.json: " + ex.Message);
            }

            return new DeviceProfilesFile();
        }

        private static void Write(DeviceProfilesFile file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            string temporaryPath = _filePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(file, _jsonOptions));
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
