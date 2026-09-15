// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommonLibTest;

[TestClass]
public sealed class AdvancedPasteSettingsTests
{
    private static readonly string[] ExpectedLocalizedHeaders =
    [
        "PasteAsPlainText_Shortcut",
        "AdvancedPasteUI_Shortcut",
        "PasteAsMarkdown_Shortcut",
        "PasteAsJson_Shortcut",
        "ImageToText",
        "FixSpellingAndGrammar",
        "PasteAsTxtFile",
        "PasteAsPngFile",
        "PasteAsHtmlFile",
        "TranscodeToMp3",
        "TranscodeToMp4",
        "LowerCase",
        "UpperCase",
        "TitleCase",
        "SentenceCase",
        "ToggleCase",
        "CamelCase",
        "PascalCase",
        "SnakeCase",
        "ScreamingSnakeCase",
        "KebabCase",
    ];

    [TestMethod]
    public void GetAllHotkeyAccessors_UsesLocalizedTextCaseHeaders()
    {
        var settings = new AdvancedPasteSettings();

        var headers = settings.GetAllHotkeyAccessors()
            .Select(accessor => accessor.LocalizationHeaderKey)
            .ToArray();

        CollectionAssert.AreEqual(ExpectedLocalizedHeaders, headers);
    }

    [TestMethod]
    public void UpgradeSettingsConfiguration_PersistsCurrentAdditionalActionShape()
    {
        var settings = new AdvancedPasteSettings
        {
            Version = "1",
        };

        Assert.IsTrue(settings.UpgradeSettingsConfiguration());
        Assert.AreEqual(AdvancedPasteSettings.ModuleVersion, settings.Version);
    }
}
