// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.PowerToys.Settings.UI.Library;

namespace PowerToys.DSC.Models.KeyboardManager;

/// <summary>
/// Converts between the friendly <see cref="KbmProfileModel"/> used by the DSC
/// profile resource and the <see cref="KeyboardManagerProfile"/> stored in the
/// Keyboard Manager profile file. The conversion mirrors the exact JSON shape
/// written by the C++ editor (MappingConfiguration::SaveSettingsToFile) so
/// that DSC-written profiles are indistinguishable from editor-written ones.
/// </summary>
public static class KbmProfileConverter
{
    // Dummy text written on run-program/open-URI entries for backwards
    // compatibility; see MappingConfiguration::SaveSettingsToFile.
    private const string UnsupportedText = "*Unsupported*";

    private const int OperationTypeRemapShortcut = 0;
    private const int OperationTypeRunProgram = 1;
    private const int OperationTypeOpenUri = 2;

    // Friendly names for the Shortcut.h enums, indexed by their numeric value.
    private static readonly string[] _elevationNames = ["normal", "elevated", "differentUser"];
    private static readonly string[] _ifRunningNames = ["showWindow", "startAnother", "doNothing", "close", "endTask"];
    private static readonly string[] _windowStyleNames = ["normal", "hidden", "minimized", "maximized"];

    /// <summary>
    /// Validates the friendly model and returns the list of validation
    /// errors; an empty list means the model is valid. The messages are
    /// intentionally not localized: they quote JSON property paths and key
    /// names that must match the configuration document verbatim, and are
    /// presented inside the localized InvalidProfileError message frame.
    /// </summary>
    /// <param name="model">The friendly model to validate.</param>
    /// <returns>The list of validation errors.</returns>
    public static IList<string> Validate(KbmProfileModel model)
    {
        var errors = new List<string>();
        var seenKeys = new List<uint>();
        var seenShortcuts = new List<(string App, KbmShortcutParser.ParsedKeys From)>();

        // JSON such as {"keys":null} or {"keys":[null]} deserializes without
        // error; guard the collections and their elements so malformed input
        // produces a validation error rather than a NullReferenceException.
        if (model.Keys == null)
        {
            errors.Add("keys must not be null");
        }

        for (var i = 0; model.Keys != null && i < model.Keys.Count; i++)
        {
            var entry = model.Keys[i];
            var context = $"keys[{i.ToString(CultureInfo.InvariantCulture)}]";

            if (entry == null)
            {
                errors.Add($"{context} must not be null");
                continue;
            }

            var targetCount = (entry.To != null ? 1 : 0) + (entry.ToText != null ? 1 : 0);
            if (targetCount != 1)
            {
                errors.Add($"{context} must set exactly one of 'to' or 'toText'");
            }

            if (!KbmShortcutParser.TryParseKey(entry.From, out var from, out var error))
            {
                errors.Add($"{context}.from: {error}");
            }
            else if (from.Keys[0] == KbmKeyNames.VkDisabled)
            {
                errors.Add($"{context}.from: 'Disable' cannot be remapped");
            }
            else if (IsGenericModifier(from.Keys[0]))
            {
                errors.Add($"{context}.from: generic modifiers must use a left or right variant");
            }
            else if (seenKeys.Find(k => KeysOverlap(k, from.Keys[0])) is var existing && existing != 0)
            {
                // Matches the editor's DoKeysOverlap: a generic modifier (e.g.
                // Ctrl) overlaps its sided variants (LCtrl/RCtrl), so both
                // cannot be remapped independently in the same profile.
                errors.Add(existing == from.Keys[0]
                    ? $"{context}.from: key '{KbmKeyNames.GetName(from.Keys[0])}' is remapped more than once"
                    : $"{context}.from: key '{KbmKeyNames.GetName(from.Keys[0])}' overlaps '{KbmKeyNames.GetName(existing)}', which is already remapped");
            }
            else
            {
                seenKeys.Add(from.Keys[0]);
            }

            if (entry.To != null)
            {
                if (!TryParseTarget(entry.To, out var target, out error))
                {
                    errors.Add($"{context}.to: {error}");
                }
                else if (from != null && IsSelfMapping(from, target))
                {
                    // Keyboard Manager rejects self-mappings, including
                    // generic/sided equivalents (e.g. CapsLock -> CapsLock).
                    errors.Add($"{context}: '{entry.From}' cannot be remapped to itself");
                }
            }

            if (entry.ToText != null && entry.ToText.Length == 0)
            {
                errors.Add($"{context}.toText must not be empty");
            }
        }

        if (model.Shortcuts == null)
        {
            errors.Add("shortcuts must not be null");
        }

        for (var i = 0; model.Shortcuts != null && i < model.Shortcuts.Count; i++)
        {
            var entry = model.Shortcuts[i];
            var context = $"shortcuts[{i.ToString(CultureInfo.InvariantCulture)}]";

            if (entry == null)
            {
                errors.Add($"{context} must not be null");
                continue;
            }

            var targetCount = (entry.To != null ? 1 : 0) + (entry.ToText != null ? 1 : 0) +
                (entry.RunProgram != null ? 1 : 0) + (entry.OpenUri != null ? 1 : 0);
            if (targetCount != 1)
            {
                errors.Add($"{context} must set exactly one of 'to', 'toText', 'runProgram', or 'openUri'");
            }

            // A supplied but blank targetApp would silently become a global
            // remap; the editor rejects blank app names, so mirror that here.
            if (entry.TargetApp != null && string.IsNullOrWhiteSpace(entry.TargetApp))
            {
                errors.Add($"{context}.targetApp must not be blank");
            }

            if (!KbmShortcutParser.TryParseKeyOrShortcut(entry.From, out var from, out var error))
            {
                errors.Add($"{context}.from: {error}");
            }
            else if (from.Keys.Count < 2)
            {
                errors.Add($"{context}.from: a shortcut requires at least one modifier and an action key");
            }
            else if (from.Keys.Contains(KbmKeyNames.VkDisabled))
            {
                errors.Add($"{context}.from: 'Disable' cannot be part of a shortcut");
            }
            else if (IsIllegalSourceShortcut(from, out var illegalName))
            {
                // Matches the editor's EditorHelpers::IsShortcutIllegal: the OS
                // handles these specially, so they cannot be used as a source.
                errors.Add($"{context}.from: shortcut '{illegalName}' is reserved by the OS and cannot be remapped");
            }
            else
            {
                var app = NormalizeTargetApp(entry.TargetApp) ?? string.Empty;
                var conflict = seenShortcuts.Find(s => s.App == app && ShortcutsOverlap(s.From, from));
                if (conflict.From != null)
                {
                    // Matches the editor's DoShortcutsOverlap: a generic modifier
                    // overlaps its sided variant, so 'Ctrl+A' and 'LCtrl+A'
                    // conflict in the same scope.
                    var scope = app.Length == 0 ? "globally" : $"for app '{app}'";
                    errors.Add(ShortcutKeysEqual(conflict.From, from)
                        ? $"{context}.from: shortcut '{KbmShortcutParser.Format(from)}' is remapped more than once {scope}"
                        : $"{context}.from: shortcut '{KbmShortcutParser.Format(from)}' overlaps '{KbmShortcutParser.Format(conflict.From)}', which is already remapped {scope}");
                }
                else
                {
                    seenShortcuts.Add((app, from));
                }
            }

            if (entry.To != null)
            {
                if (!TryParseTarget(entry.To, out var target, out error))
                {
                    errors.Add($"{context}.to: {error}");
                }
                else if (from != null && IsSelfMapping(from, target))
                {
                    // The editor rejects shortcut self-mappings (e.g. Ctrl+A ->
                    // Ctrl+A), including generic/sided equivalents.
                    errors.Add($"{context}: '{entry.From}' cannot be remapped to itself");
                }
            }

            if (entry.ToText != null && entry.ToText.Length == 0)
            {
                errors.Add($"{context}.toText must not be empty");
            }

            if (entry.OpenUri != null && string.IsNullOrWhiteSpace(entry.OpenUri))
            {
                errors.Add($"{context}.openUri must not be empty or whitespace");
            }

            if (entry.RunProgram != null)
            {
                if (string.IsNullOrWhiteSpace(entry.RunProgram.FilePath))
                {
                    errors.Add($"{context}.runProgram.filePath must not be empty");
                }

                ValidateEnumName(entry.RunProgram.Elevation, _elevationNames, $"{context}.runProgram.elevation", errors);
                ValidateEnumName(entry.RunProgram.IfRunning, _ifRunningNames, $"{context}.runProgram.ifRunning", errors);
                ValidateEnumName(entry.RunProgram.WindowStyle, _windowStyleNames, $"{context}.runProgram.windowStyle", errors);
            }
        }

        return errors;
    }

    /// <summary>
    /// Converts a validated friendly model to the stored profile shape.
    /// </summary>
    /// <param name="model">The friendly model; must have passed <see cref="Validate"/>.</param>
    /// <returns>The stored profile.</returns>
    public static KeyboardManagerProfile ToProfile(KbmProfileModel model)
    {
        var profile = new KeyboardManagerProfile();

        foreach (var entry in model.Keys)
        {
            if (!KbmShortcutParser.TryParseKey(entry.From, out var from, out var error))
            {
                throw new InvalidOperationException(error);
            }

            var stored = new KeysDataModel
            {
                OriginalKeys = from.ToVkString(),
            };

            if (entry.ToText != null)
            {
                stored.NewRemapString = entry.ToText;
                profile.RemapKeysToText.InProcessRemapKeys.Add(stored);
            }
            else
            {
                stored.NewRemapKeys = ParseTargetOrThrow(entry.To!).ToVkString();
                profile.RemapKeys.InProcessRemapKeys.Add(stored);
            }
        }

        foreach (var entry in model.Shortcuts)
        {
            if (!KbmShortcutParser.TryParseKeyOrShortcut(entry.From, out var from, out var error))
            {
                throw new InvalidOperationException(error);
            }

            var app = NormalizeTargetApp(entry.TargetApp);
            var stored = app != null ? new AppSpecificKeysDataModel { TargetApp = app } : new KeysDataModel();
            stored.OriginalKeys = from.ToVkString();
            stored.ExactMatch = entry.ExactMatch ?? false;

            var isText = false;
            if (entry.ToText != null)
            {
                stored.NewRemapString = entry.ToText;
                isText = true;
            }
            else if (entry.RunProgram != null)
            {
                stored.OperationType = OperationTypeRunProgram;
                stored.RunProgramFilePath = entry.RunProgram.FilePath;
                stored.RunProgramArgs = entry.RunProgram.Args ?? string.Empty;
                stored.RunProgramStartInDir = entry.RunProgram.StartInDir ?? string.Empty;
                stored.RunProgramElevationLevel = ParseEnumName(entry.RunProgram.Elevation, _elevationNames);
                stored.RunProgramAlreadyRunningAction = ParseEnumName(entry.RunProgram.IfRunning, _ifRunningNames);
                stored.RunProgramStartWindowType = ParseEnumName(entry.RunProgram.WindowStyle, _windowStyleNames);
                stored.NewRemapString = UnsupportedText;
            }
            else if (entry.OpenUri != null)
            {
                stored.OperationType = OperationTypeOpenUri;
                stored.OpenUri = entry.OpenUri;
                stored.RunProgramElevationLevel = 0;
                stored.NewRemapString = UnsupportedText;
            }
            else
            {
                var target = ParseTargetOrThrow(entry.To!);
                stored.NewRemapKeys = target.ToVkString();
                if (!target.IsSingleKey)
                {
                    stored.OperationType = OperationTypeRemapShortcut;
                }
            }

            var section = isText ? profile.RemapShortcutsToText : profile.RemapShortcuts;
            if (stored is AppSpecificKeysDataModel appStored)
            {
                section.AppSpecificRemapShortcuts.Add(appStored);
            }
            else
            {
                section.GlobalRemapShortcuts.Add(stored);
            }
        }

        return profile;
    }

    /// <summary>
    /// Converts a stored profile to the canonical friendly model. Entries
    /// that cannot be parsed are skipped with a warning, mirroring the
    /// engine's tolerance for malformed entries.
    /// </summary>
    /// <param name="profile">The stored profile.</param>
    /// <param name="warnings">Optional collector for warnings about skipped entries.</param>
    /// <returns>The canonical friendly model.</returns>
    public static KbmProfileModel FromProfile(KeyboardManagerProfile profile, IList<string>? warnings = null)
    {
        var keys = new List<(uint Code, KbmKeyRemapEntry Entry)>();
        var shortcuts = new List<KbmShortcutRemapEntry>();

        foreach (var stored in profile.RemapKeys?.InProcessRemapKeys ?? [])
        {
            if (stored == null)
            {
                warnings?.Add("Skipping a null key remap entry");
                continue;
            }

            if (!KbmShortcutParser.TryParseVkString(stored.OriginalKeys, 0, out var from) || !from.IsSingleKey ||
                !KbmShortcutParser.TryParseVkString(stored.NewRemapKeys, 0, out var to))
            {
                warnings?.Add($"Skipping unparsable key remap entry '{stored.OriginalKeys}'");
                continue;
            }

            var keyTarget = KbmShortcutParser.Format(KbmShortcutParser.Canonicalize(to));

            // Ensure the exported target is one Validate accepts on a subsequent
            // set (e.g. a modifier-only stored value would render as 'Ctrl+Alt',
            // which is not a supported target); skip it with a warning otherwise.
            if (!TryParseTarget(keyTarget, out _, out _))
            {
                warnings?.Add($"Skipping key remap entry '{stored.OriginalKeys}' with an unsupported target '{keyTarget}'");
                continue;
            }

            keys.Add((from.Keys[0], new KbmKeyRemapEntry
            {
                From = KbmKeyNames.GetName(from.Keys[0]),
                To = keyTarget,
            }));
        }

        foreach (var stored in profile.RemapKeysToText?.InProcessRemapKeys ?? [])
        {
            if (stored == null)
            {
                warnings?.Add("Skipping a null key-to-text remap entry");
                continue;
            }

            if (!KbmShortcutParser.TryParseVkString(stored.OriginalKeys, 0, out var from) || !from.IsSingleKey ||
                string.IsNullOrEmpty(stored.NewRemapString))
            {
                warnings?.Add($"Skipping unparsable key-to-text remap entry '{stored.OriginalKeys}'");
                continue;
            }

            keys.Add((from.Keys[0], new KbmKeyRemapEntry
            {
                From = KbmKeyNames.GetName(from.Keys[0]),
                ToText = stored.NewRemapString,
            }));
        }

        foreach (var (stored, app) in EnumerateShortcuts(profile.RemapShortcuts, warnings))
        {
            var entry = CreateShortcutEntry(stored, app, warnings);
            if (entry == null)
            {
                continue;
            }

            if (stored.OperationType == OperationTypeRunProgram)
            {
                if (string.IsNullOrWhiteSpace(stored.RunProgramFilePath))
                {
                    warnings?.Add($"Skipping run-program remap entry '{stored.OriginalKeys}' without a program path");
                    continue;
                }

                entry.RunProgram = new KbmRunProgramAction
                {
                    FilePath = stored.RunProgramFilePath,
                    Args = NullIfEmpty(stored.RunProgramArgs),
                    StartInDir = NullIfEmpty(stored.RunProgramStartInDir),
                    Elevation = FormatEnumValue(stored.RunProgramElevationLevel, _elevationNames, $"runProgram.elevation for '{stored.OriginalKeys}'", warnings),
                    IfRunning = FormatEnumValue(stored.RunProgramAlreadyRunningAction, _ifRunningNames, $"runProgram.ifRunning for '{stored.OriginalKeys}'", warnings),
                    WindowStyle = FormatEnumValue(stored.RunProgramStartWindowType, _windowStyleNames, $"runProgram.windowStyle for '{stored.OriginalKeys}'", warnings),
                };
            }
            else if (stored.OperationType == OperationTypeOpenUri)
            {
                if (string.IsNullOrWhiteSpace(stored.OpenUri))
                {
                    warnings?.Add($"Skipping open-URI remap entry '{stored.OriginalKeys}' without a URI");
                    continue;
                }

                entry.OpenUri = stored.OpenUri;
            }
            else
            {
                if (!KbmShortcutParser.TryParseVkString(stored.NewRemapKeys, 0, out var to))
                {
                    warnings?.Add($"Skipping unparsable shortcut remap entry '{stored.OriginalKeys}'");
                    continue;
                }

                var target = KbmShortcutParser.Format(KbmShortcutParser.Canonicalize(to));

                // Ensure the exported target is one that Validate accepts on a
                // subsequent set (e.g. a numeric-only stored value like '17;18'
                // would render as a modifier-only 'Ctrl+Alt', which is not a
                // supported target); skip it with a warning otherwise so the
                // exported state stays importable.
                if (!TryParseTarget(target, out _, out _))
                {
                    warnings?.Add($"Skipping shortcut remap entry '{stored.OriginalKeys}' with an unsupported target '{target}'");
                    continue;
                }

                entry.To = target;
            }

            shortcuts.Add(entry);
        }

        foreach (var (stored, app) in EnumerateShortcuts(profile.RemapShortcutsToText, warnings))
        {
            var entry = CreateShortcutEntry(stored, app, warnings);
            if (entry == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(stored.NewRemapString))
            {
                warnings?.Add($"Skipping shortcut-to-text remap entry '{stored.OriginalKeys}' without text");
                continue;
            }

            entry.ToText = stored.NewRemapString;
            shortcuts.Add(entry);
        }

        var result = new KbmProfileModel
        {
            Keys = keys.OrderBy(k => k.Code).Select(k => k.Entry).ToList(),
            Shortcuts = shortcuts
                .OrderBy(s => s.TargetApp ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(s => s.From, StringComparer.Ordinal)
                .ToList(),
        };

        // Each entry above is individually parseable, but the C++ engine also
        // tolerates cross-entry combinations this resource's Validate rejects
        // (a self-mapping such as CapsLock -> CapsLock, an OS-reserved source
        // like Win+L, a repeated modifier class, or two overlapping sources in
        // the same scope). Drop those so the exported state stays importable by
        // a subsequent set.
        RemoveNonImportableEntries(result, warnings);
        return result;
    }

    private static readonly Regex EntryContextRegex = new(@"^(keys|shortcuts)\[(\d+)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Removes entries from an assembled export model that would fail this
    /// resource's own <see cref="Validate"/>, reusing its self-mapping,
    /// overlap, reserved-shortcut, and repeated-modifier rules rather than
    /// duplicating them. Validate reports errors keyed by "keys[i]" and
    /// "shortcuts[i]" context; the offending entries are dropped (highest
    /// index first so lower indices stay valid) and the model is re-validated
    /// until it is clean. Overlap and duplicate errors reference the later,
    /// conflicting entry, so the first occurrence of each source is preserved.
    /// </summary>
    private static void RemoveNonImportableEntries(KbmProfileModel model, IList<string>? warnings)
    {
        while (true)
        {
            var errors = Validate(model);
            if (errors.Count == 0)
            {
                return;
            }

            var keyIndices = new SortedSet<int>();
            var shortcutIndices = new SortedSet<int>();
            foreach (var error in errors)
            {
                var match = EntryContextRegex.Match(error);
                if (!match.Success)
                {
                    continue;
                }

                var index = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                if (match.Groups[1].Value == "keys")
                {
                    keyIndices.Add(index);
                }
                else
                {
                    shortcutIndices.Add(index);
                }
            }

            if (keyIndices.Count == 0 && shortcutIndices.Count == 0)
            {
                // No entry-scoped error we can act on; stop to avoid looping.
                return;
            }

            foreach (var index in keyIndices.Reverse())
            {
                warnings?.Add($"Skipping key remap entry '{model.Keys[index].From}' that is not importable");
                model.Keys.RemoveAt(index);
            }

            foreach (var index in shortcutIndices.Reverse())
            {
                warnings?.Add($"Skipping shortcut remap entry '{model.Shortcuts[index].From}' that is not importable");
                model.Shortcuts.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Normalizes a friendly model into its canonical form: canonical key
    /// spellings and ordering, default-valued fields omitted, and entries
    /// sorted. Used to compare desired and current state.
    /// </summary>
    /// <param name="model">The friendly model; must have passed <see cref="Validate"/>.</param>
    /// <returns>The canonical friendly model.</returns>
    public static KbmProfileModel Canonicalize(KbmProfileModel model)
    {
        // Round-tripping through the stored shape guarantees that the desired
        // state and the state read back from disk normalize identically.
        return FromProfile(ToProfile(model));
    }

    private static IEnumerable<(KeysDataModel Stored, string? App)> EnumerateShortcuts(ShortcutsKeyDataModel? section, IList<string>? warnings)
    {
        foreach (var stored in section?.GlobalRemapShortcuts ?? [])
        {
            if (stored == null)
            {
                // A null element (e.g. "global":[null]) is a load failure for
                // the C++ engine; warn so NeedsUpdate() forces a rewrite rather
                // than silently comparing equal to an empty desired profile.
                warnings?.Add("Skipping a null global shortcut remap entry");
                continue;
            }

            yield return (stored, null);
        }

        foreach (var stored in section?.AppSpecificRemapShortcuts ?? [])
        {
            if (stored == null)
            {
                warnings?.Add("Skipping a null app-specific shortcut remap entry");
                continue;
            }

            // An app-specific entry must carry a non-blank target process name.
            // The engine rejects one whose targetApp is missing/blank (its
            // GetNamedString("targetApp") fails); yielding it as (app == null)
            // would silently widen it to a global remap - including a
            // run-program action - so skip it with a warning instead.
            if (string.IsNullOrWhiteSpace(stored.TargetApp))
            {
                warnings?.Add($"Skipping app-specific shortcut remap entry '{stored.OriginalKeys}' with a blank target application");
                continue;
            }

            yield return (stored, stored.TargetApp);
        }
    }

    private static KbmShortcutRemapEntry? CreateShortcutEntry(KeysDataModel stored, string? app, IList<string>? warnings)
    {
        if (!KbmShortcutParser.TryParseVkString(stored.OriginalKeys, stored.SecondKeyOfChord, out var from) || from.Keys.Count < 2)
        {
            warnings?.Add($"Skipping unparsable shortcut remap entry '{stored.OriginalKeys}'");
            return null;
        }

        // The chord second key is embedded as the trailing element of the
        // stored key string; detect it even when the secondKeyOfChord
        // property is absent (it is not written by the C++ editor).
        if (from.SecondKeyOfChord == 0 && from.Keys.Count >= 3 &&
            !KbmKeyNames.IsModifier(from.Keys[^1]) && !KbmKeyNames.IsModifier(from.Keys[^2]))
        {
            from = new KbmShortcutParser.ParsedKeys(from.Keys, from.Keys[^1]);
        }

        // A stored key list of the right length is not necessarily a valid
        // shortcut: a modifier-only source ('Ctrl+Alt'), a modifier-less one
        // ('A, B'), or one with more than one first-stage action ('Ctrl+A+B, C')
        // would be exported but rejected by Validate on re-import. Require at
        // least one modifier and exactly one first-stage action, and no disabled
        // key, so exported state remains importable.
        var (modifiers, _, chord) = DecomposeShortcut(from);
        var firstStageActions = from.Keys.Count(k => !KbmKeyNames.IsModifier(k)) - (chord != 0 ? 1 : 0);
        if (modifiers.Count == 0 || firstStageActions != 1 || from.Keys.Contains(KbmKeyNames.VkDisabled))
        {
            warnings?.Add($"Skipping shortcut remap entry '{stored.OriginalKeys}' that is not a valid shortcut");
            return null;
        }

        // Preserve the engine's exact process scope. It lower-cases stored app
        // names but does not trim them, so exporting a whitespace-padded name
        // as a trimmed name would activate the remap for a different process.
        if (app != null && app != app.Trim())
        {
            warnings?.Add($"Skipping app-specific shortcut remap entry '{stored.OriginalKeys}' with surrounding whitespace in its target application");
            return null;
        }

        return new KbmShortcutRemapEntry
        {
            From = KbmShortcutParser.Format(KbmShortcutParser.Canonicalize(from)),
            TargetApp = app?.ToLowerInvariant(),
            ExactMatch = stored.ExactMatch == true ? true : null,
        };
    }

    // Virtual-key codes of the generic (side-agnostic) modifiers.
    private const uint VkCtrl = 17;
    private const uint VkAlt = 18;
    private const uint VkShift = 16;
    private const uint VkL = 76;
    private const uint VkDelete = 46;

    private static bool IsGenericModifier(uint code)
    {
        return code is VkCtrl or VkAlt or VkShift or KbmKeyNames.VkWinBoth;
    }

    /// <summary>
    /// Determines whether two single keys overlap, mirroring the editor's
    /// DoKeysOverlap: a generic modifier (e.g. Ctrl) overlaps its sided
    /// variants (LCtrl/RCtrl); two different sided variants do not overlap.
    /// </summary>
    private static bool KeysOverlap(uint a, uint b)
    {
        if (a == b)
        {
            return true;
        }

        var classA = KbmKeyNames.GetModifierClass(a);
        var classB = KbmKeyNames.GetModifierClass(b);
        if (classA == KbmKeyNames.ModifierClass.None || classA != classB)
        {
            return false;
        }

        return IsGenericModifier(a) || IsGenericModifier(b);
    }

    private static (List<uint> Modifiers, uint Action, uint Chord) DecomposeShortcut(KbmShortcutParser.ParsedKeys s)
    {
        var modifiers = new List<uint>();
        var action = 0u;
        var chord = s.SecondKeyOfChord;
        var lastIndex = s.Keys.Count - 1;

        for (var i = 0; i < s.Keys.Count; i++)
        {
            var key = s.Keys[i];
            if (KbmKeyNames.IsModifier(key))
            {
                modifiers.Add(key);
            }
            else if (chord != 0 && i == lastIndex)
            {
                // The trailing element is the chord's second key; exclude it by
                // position so a chord that repeats the action key (e.g.
                // 'Ctrl+A, A') still resolves the correct first-stage action.
            }
            else
            {
                action = key;
            }
        }

        return (modifiers, action, chord);
    }

    private static bool ShortcutKeysEqual(KbmShortcutParser.ParsedKeys a, KbmShortcutParser.ParsedKeys b)
    {
        return a.SecondKeyOfChord == b.SecondKeyOfChord && a.Keys.SequenceEqual(b.Keys);
    }

    /// <summary>
    /// Determines whether a remap maps a source onto itself, which the editor
    /// rejects (ValidationHelper.IsSelfMapping). Single keys are compared with
    /// the generic/sided overlap rule; shortcuts with the shortcut overlap rule.
    /// </summary>
    private static bool IsSelfMapping(KbmShortcutParser.ParsedKeys from, KbmShortcutParser.ParsedKeys to)
    {
        if (from.Keys.Count == 1 && to.Keys.Count == 1)
        {
            return KeysOverlap(from.Keys[0], to.Keys[0]);
        }

        // A shortcut self-mapping requires exact equality (sided modifiers and
        // chord included), matching the editor's Shortcut::operator==; the
        // looser overlap rule used for duplicate detection would wrongly reject
        // mappings like 'LCtrl+A -> Ctrl+A' that the editor accepts.
        return ShortcutKeysEqual(KbmShortcutParser.Canonicalize(from), KbmShortcutParser.Canonicalize(to));
    }

    /// <summary>
    /// Determines whether two shortcuts overlap, mirroring the editor's
    /// DoShortcutsOverlap: they must share the same action key and set of
    /// modifier classes, and within each class the specific keys must be
    /// compatible (equal or one of them a generic modifier). Differing chords
    /// normally distinguish two shortcuts, but when a generic modifier makes
    /// the first stage ambiguous both mappings still compete, so they overlap.
    /// </summary>
    private static bool ShortcutsOverlap(KbmShortcutParser.ParsedKeys a, KbmShortcutParser.ParsedKeys b)
    {
        var (modsA, actionA, chordA) = DecomposeShortcut(a);
        var (modsB, actionB, chordB) = DecomposeShortcut(b);
        if (actionA != actionB)
        {
            return false;
        }

        var classesA = modsA.Select(KbmKeyNames.GetModifierClass).ToHashSet();
        var classesB = modsB.Select(KbmKeyNames.GetModifierClass).ToHashSet();
        if (!classesA.SetEquals(classesB))
        {
            return false;
        }

        var firstStageAmbiguous = false;
        foreach (var cls in classesA)
        {
            var keyA = modsA.First(k => KbmKeyNames.GetModifierClass(k) == cls);
            var keyB = modsB.First(k => KbmKeyNames.GetModifierClass(k) == cls);
            if (keyA != keyB)
            {
                if (!IsGenericModifier(keyA) && !IsGenericModifier(keyB))
                {
                    return false;
                }

                firstStageAmbiguous = true;
            }
            else if (IsGenericModifier(keyA))
            {
                // A generic modifier (ModifierKey::Both) matches either side, so
                // it makes the first stage ambiguous even when the codes match.
                firstStageAmbiguous = true;
            }
        }

        // Identical (sided) first stages are distinct chord shortcuts, so their
        // differing chords do not conflict; an ambiguous first stage (any
        // generic modifier) makes differing chords conflict as well.
        return chordA == chordB || firstStageAmbiguous;
    }

    /// <summary>
    /// Determines whether a source shortcut is one the OS reserves and the
    /// editor rejects (EditorHelpers::IsShortcutIllegal), such as Win+L or
    /// Ctrl+Alt+Delete, which cannot be intercepted as a remap source.
    /// </summary>
    private static bool IsIllegalSourceShortcut(KbmShortcutParser.ParsedKeys from, out string? name)
    {
        name = null;

        // Evaluate the first-stage shortcut (modifiers + action); the chord's
        // second key is ignored because the OS intercepts the first stage
        // (e.g. 'Win+L, X' is still blocked at 'Win+L').
        var (modifiers, action, _) = DecomposeShortcut(from);
        var classes = modifiers.Select(KbmKeyNames.GetModifierClass).ToHashSet();

        // Win+L (lock workstation)
        if (action == VkL && classes.SetEquals(new[] { KbmKeyNames.ModifierClass.Win }))
        {
            name = "Win+L";
            return true;
        }

        // Ctrl+Alt+Delete (secure attention sequence)
        if (action == VkDelete && classes.SetEquals(new[] { KbmKeyNames.ModifierClass.Ctrl, KbmKeyNames.ModifierClass.Alt }))
        {
            name = "Ctrl+Alt+Delete";
            return true;
        }

        return false;
    }

    private static bool TryParseTarget(string input, out KbmShortcutParser.ParsedKeys result, out string error)
    {
        // A remap target may be a single key (including punctuation aliases such
        // as "," and lone modifiers) or a shortcut; chords are origin-only. Try
        // the single-key parse first so a key whose name contains a separator
        // (e.g. ",") is not sent to the shortcut parser by mistake.
        if (KbmShortcutParser.TryParseKey(input, out result, out error))
        {
            return true;
        }

        if (!input.Contains('+', StringComparison.Ordinal) && !input.Contains(',', StringComparison.Ordinal))
        {
            // No separators: the single-key parse failure is the real error.
            return false;
        }

        if (!KbmShortcutParser.TryParseKeyOrShortcut(input, out result, out error))
        {
            return false;
        }

        if (result.SecondKeyOfChord != 0)
        {
            error = $"Chords are not supported in remap targets ('{input.Trim()}')";
            return false;
        }

        // 'Disable' is only meaningful as a lone target; the engine does not
        // treat VK_DISABLED as the disable action when combined with other keys.
        if (result.Keys.Count > 1 && result.Keys.Contains(KbmKeyNames.VkDisabled))
        {
            error = $"'Disable' can only be used as a single-key target ('{input.Trim()}')";
            return false;
        }

        return true;
    }

    private static KbmShortcutParser.ParsedKeys ParseTargetOrThrow(string input)
    {
        if (!TryParseTarget(input, out var result, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return result;
    }

    private static void ValidateEnumName(string? name, string[] names, string context, IList<string> errors)
    {
        if (name != null && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"{context}: invalid value '{name}'; allowed values are: {string.Join(", ", names)}");
        }
    }

    private static int ParseEnumName(string? name, string[] names)
    {
        if (name == null)
        {
            return 0;
        }

        var index = Array.FindIndex(names, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : throw new InvalidOperationException($"Invalid value '{name}'");
    }

    private static string? FormatEnumValue(int? value, string[] names, string context, IList<string>? warnings)
    {
        // Default (0) values are omitted from the canonical form.
        if (value is null or 0)
        {
            return null;
        }

        if (value < 0 || value >= names.Length)
        {
            // The stored value is outside the range this resource understands
            // (e.g. written by a newer engine). Surface it instead of silently
            // normalizing to the default, which would hide configuration drift.
            warnings?.Add($"{context}: stored value '{value}' is out of range and was omitted from the exported state");
            return null;
        }

        return names[value.Value];
    }

    private static string? NormalizeTargetApp(string? app)
    {
        if (string.IsNullOrWhiteSpace(app))
        {
            return null;
        }

        // The engine lower-cases the target app on load; mirror that here
        return app.Trim().ToLowerInvariant();
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
