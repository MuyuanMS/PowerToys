// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using WorkspacesLauncherUI.Helpers;

namespace WorkspacesLauncherUI.UnitTests
{
    [TestClass]
    public class IconHelperTests
    {
        private string _testDirectory;

        [TestInitialize]
        public void Initialize()
        {
            _testDirectory = Path.Combine(AppContext.BaseDirectory, nameof(IconHelperTests), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.Delete(_testDirectory, true);
        }

        [TestMethod]
        [TestCategory("Helpers")]
        public void ResolvePackagedLogoPath_UnqualifiedAssetExists_ReturnsUnqualifiedPath()
        {
            string path = CreateFile("Logo.png");

            Assert.AreEqual(path, IconHelper.ResolvePackagedLogoPath(path));
        }

        [TestMethod]
        [TestCategory("Helpers")]
        public void ResolvePackagedLogoPath_TargetSizeAssetExists_ReturnsPreferredTargetSize()
        {
            string path = Path.Combine(_testDirectory, "Logo.png");
            CreateFile("Logo.targetsize-44.png");
            string preferredPath = CreateFile("Logo.targetsize-36.png");

            Assert.AreEqual(preferredPath, IconHelper.ResolvePackagedLogoPath(path));
        }

        [TestMethod]
        [TestCategory("Helpers")]
        public void ResolvePackagedLogoPath_UnplatedAssetExists_ReturnsUnplatedPath()
        {
            string path = Path.Combine(_testDirectory, "Logo.png");
            string unplatedPath = CreateFile("Logo.targetsize-36_altform-unplated.png");

            Assert.AreEqual(unplatedPath, IconHelper.ResolvePackagedLogoPath(path));
        }

        [TestMethod]
        [TestCategory("Helpers")]
        public void ResolvePackagedLogoPath_ScaleAssetExists_ReturnsScalePath()
        {
            string path = Path.Combine(_testDirectory, "Logo.png");
            string scalePath = CreateFile("Logo.scale-200.png");

            Assert.AreEqual(scalePath, IconHelper.ResolvePackagedLogoPath(path));
        }

        private string CreateFile(string fileName)
        {
            string path = Path.Combine(_testDirectory, fileName);
            File.WriteAllBytes(path, []);
            return path;
        }
    }
}
