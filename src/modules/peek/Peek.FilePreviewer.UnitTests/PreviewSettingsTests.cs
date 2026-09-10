// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Peek.FilePreviewer.Models;
using Settings.UI.Library;

namespace Peek.FilePreviewer.UnitTests
{
    [TestClass]
    public class PreviewSettingsTests
    {
        [TestMethod]
        public void GetSourceCodeMaxFileSizeBytes_ValueBelowMinimum_ShouldClamp()
        {
            long result = PreviewSettings.GetSourceCodeMaxFileSizeBytes(0);

            Assert.AreEqual((long)PeekPreviewSettings.MinSourceCodeMaxFileSize * 1024, result);
        }

        [TestMethod]
        public void GetSourceCodeMaxFileSizeBytes_ValueAboveMaximum_ShouldClamp()
        {
            long result = PreviewSettings.GetSourceCodeMaxFileSizeBytes(int.MaxValue);

            Assert.AreEqual((long)PeekPreviewSettings.MaxSourceCodeMaxFileSize * 1024, result);
        }

        [TestMethod]
        public void GetSourceCodeMaxFileSizeBytes_ValueWithinRange_ShouldConvertToBytes()
        {
            long result = PreviewSettings.GetSourceCodeMaxFileSizeBytes(2048);

            Assert.AreEqual(2048L * 1024, result);
        }

        [TestMethod]
        public void Deserialize_OlderSettingsWithoutMaxFileSize_ShouldRetainDefault()
        {
            var settings = JsonSerializer.Deserialize(
                "{}",
                SettingsSerializationContext.Default.PeekPreviewSettings);

            Assert.IsNotNull(settings);
            Assert.AreEqual(PeekPreviewSettings.DefaultSourceCodeMaxFileSize, settings.SourceCodeMaxFileSize.Value);
        }

        [TestMethod]
        public void SourceCodeMaxFileSize_ShouldSurviveJsonRoundTrip()
        {
            var original = new PeekPreviewSettings();
            original.SourceCodeMaxFileSize.Value = 2048;

            var deserialized = JsonSerializer.Deserialize(
                original.ToJsonString(),
                SettingsSerializationContext.Default.PeekPreviewSettings);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(2048, deserialized.SourceCodeMaxFileSize.Value);
        }
    }
}
