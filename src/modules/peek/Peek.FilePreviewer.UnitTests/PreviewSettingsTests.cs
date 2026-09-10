// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
    }
}
