// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ViewModelTests
{
    [TestClass]
    public class PowerDisplay
    {
        [TestMethod]
        public void WarningResourcesShouldExistForEveryWarningKind()
        {
            var resources = LoadResourceNames();

            Assert.IsTrue(resources.Contains("PowerDisplay_Warning_Title"));
            Assert.IsTrue(resources.Contains("PowerDisplay_Warning_LearnMore"));
            Assert.IsTrue(resources.Contains("PowerDisplay_Warning_Default_InfoBar"));
            Assert.IsTrue(resources.Contains("PowerDisplay_Warning_Default_Body"));

            foreach (var kind in Enum.GetNames(typeof(PowerDisplayWarningKind)))
            {
                Assert.IsTrue(resources.Contains($"PowerDisplay_Warning_{kind}_InfoBar"), $"Missing InfoBar resource for {kind}.");
                Assert.IsTrue(resources.Contains($"PowerDisplay_Warning_{kind}_Body"), $"Missing body resource for {kind}.");
            }
        }

        private static HashSet<string> LoadResourceNames()
        {
            var resourceFile = FindResourceFile();
            var document = XDocument.Load(resourceFile);

            return document.Root?
                .Elements("data")
                .Select(element => element.Attribute("name")?.Value)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
        }

        private static string FindResourceFile()
        {
            for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName, "src", "settings-ui", "Settings.UI", "Strings", "en-us", "Resources.resw");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            Assert.Fail("Could not locate src\\settings-ui\\Settings.UI\\Strings\\en-us\\Resources.resw from the test output folder.");
            return string.Empty;
        }
    }
}
