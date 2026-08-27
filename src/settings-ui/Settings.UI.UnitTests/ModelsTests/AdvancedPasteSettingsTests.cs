// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommonLibTest;

[TestClass]
public class AdvancedPasteSettingsTests
{
    [TestMethod]
    public void DeserializeWithoutJpgQuality_UsesDefault()
    {
        const string json = """{"properties":{}}""";

        var settings = JsonSerializer.Deserialize<AdvancedPasteSettings>(json);

        Assert.IsNotNull(settings);
        Assert.AreEqual(AdvancedPasteProperties.DefaultPasteAsJpgQuality, settings.Properties.PasteAsJpgQuality.Value);
    }

    [TestMethod]
    public void JpgQuality_RoundTripsNonDefaultValue()
    {
        var settings = new AdvancedPasteSettings();
        settings.Properties.PasteAsJpgQuality.Value = 67;

        var deserialized = JsonSerializer.Deserialize<AdvancedPasteSettings>(JsonSerializer.Serialize(settings));

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(67, deserialized.Properties.PasteAsJpgQuality.Value);
    }
}
