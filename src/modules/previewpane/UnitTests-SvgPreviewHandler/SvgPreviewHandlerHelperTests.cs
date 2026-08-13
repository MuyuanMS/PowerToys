// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text;

using Common.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SvgPreviewHandlerUnitTests
{
    [STATestClass]
    public class SvgPreviewHandlerHelperTests
    {
        [TestMethod]
        public void CheckBlockedElementsShouldReturnTrueIfABlockedElementIsPresent()
        {
            // Arrange
            var svgBuilder = new StringBuilder();
            svgBuilder.AppendLine("<svg width =\"200\" height=\"200\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">");
            svgBuilder.AppendLine("\t<script>alert(\"hello\")</script>");
            svgBuilder.AppendLine("</svg>");
            bool foundFilteredElement;

            // Act
            foundFilteredElement = SvgPreviewHandlerHelper.CheckBlockedElements(svgBuilder.ToString());

            // Assert
            Assert.IsTrue(foundFilteredElement);
        }

        [TestMethod]
        public void CheckBlockedElementsShouldReturnTrueIfBlockedElementsIsPresentInNestedLevel()
        {
            // Arrange
            var svgBuilder = new StringBuilder();
            svgBuilder.AppendLine("<svg viewBox=\"0 0 100 100\" xmlns=\"http://www.w3.org/2000/svg\">");
            svgBuilder.AppendLine("\t<circle cx=\"50\" cy=\"50\" r=\"50\">");
            svgBuilder.AppendLine("\t\t<script>alert(\"valid-message\")</script>");
            svgBuilder.AppendLine("\t</circle>");
            svgBuilder.AppendLine("</svg>");
            bool foundFilteredElement;

            // Act
            foundFilteredElement = SvgPreviewHandlerHelper.CheckBlockedElements(svgBuilder.ToString());

            // Assert
            Assert.IsTrue(foundFilteredElement);
        }

        [TestMethod]
        public void CheckBlockedElementsShouldReturnTrueIfMultipleBlockedElementsArePresent()
        {
            // Arrange
            var svgBuilder = new StringBuilder();
            svgBuilder.AppendLine("<svg width =\"200\" height=\"200\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">");
            svgBuilder.AppendLine("\t<script>alert(\"valid-message\")</script>");
            svgBuilder.AppendLine("\t<image href=\"valid-url\" height=\"200\" width=\"200\"/>");
            svgBuilder.AppendLine("</svg>");
            bool foundFilteredElement;

            // Act
            foundFilteredElement = SvgPreviewHandlerHelper.CheckBlockedElements(svgBuilder.ToString());

            // Assert
            Assert.IsTrue(foundFilteredElement);
        }

        [TestMethod]
        public void CheckBlockedElementsShouldReturnFalseIfNoBlockedElementsArePresent()
        {
            // Arrange
            var svgBuilder = new StringBuilder();
            svgBuilder.AppendLine("<svg viewBox=\"0 0 100 100\" xmlns=\"http://www.w3.org/2000/svg\">");
            svgBuilder.AppendLine("\t<circle cx=\"50\" cy=\"50\" r=\"50\">");
            svgBuilder.AppendLine("\t</circle>");
            svgBuilder.AppendLine("</svg>");
            bool foundFilteredElement;

            // Act
            foundFilteredElement = SvgPreviewHandlerHelper.CheckBlockedElements(svgBuilder.ToString());

            // Assert
            Assert.IsFalse(foundFilteredElement);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("  ")]
        [DataRow(null)]
        public void CheckBlockedElementsShouldReturnFalseIfSvgDataIsNullOrWhiteSpaces(string svgData)
        {
            // Arrange
            bool foundFilteredElement;

            // Act
            foundFilteredElement = SvgPreviewHandlerHelper.CheckBlockedElements(svgData);

            // Assert
            Assert.IsFalse(foundFilteredElement);
        }

        [TestMethod]
        public void BuildCacheKeyShouldReturnSameValueForSameInputs()
        {
            // Arrange
            var firstKey = SvgPreviewCacheHelper.BuildCacheKey("v1", "svg-preview", "sample data");

            // Act
            var secondKey = SvgPreviewCacheHelper.BuildCacheKey("v1", "svg-preview", "sample data");

            // Assert
            Assert.AreEqual(firstKey, secondKey);
        }

        [TestMethod]
        public void BuildCacheKeyShouldReturnDifferentValueForDifferentInputs()
        {
            // Arrange
            var firstKey = SvgPreviewCacheHelper.BuildCacheKey("v1", "svg-preview", "sample data");

            // Act
            var secondKey = SvgPreviewCacheHelper.BuildCacheKey("v1", "svg-preview", "different data");

            // Assert
            Assert.AreNotEqual(firstKey, secondKey);
        }

        [TestMethod]
        public void BuildCacheKeyShouldDistinguishInputsContainingDelimiters()
        {
            var firstKey = SvgPreviewCacheHelper.BuildCacheKey("a\nb", string.Empty);
            var secondKey = SvgPreviewCacheHelper.BuildCacheKey("a", "b\n");

            Assert.AreNotEqual(firstKey, secondKey);
        }

        [TestMethod]
        public void TryWriteCacheFileAtomicShouldCreateReusableEntry()
        {
            var cacheFolder = CreateTestCacheFolder();

            try
            {
                var cacheKey = SvgPreviewCacheHelper.BuildCacheKey("atomic-write");

                Assert.IsTrue(SvgPreviewCacheHelper.TryWriteCacheFileAtomic(cacheFolder, cacheKey, "contents", out var writtenPath));
                Assert.IsTrue(SvgPreviewCacheHelper.TryGetCacheFile(cacheFolder, cacheKey, out var reusedPath));
                Assert.AreEqual(writtenPath, reusedPath);
                Assert.AreEqual("contents", File.ReadAllText(reusedPath));
                Assert.AreEqual(0, Directory.GetFiles(cacheFolder, "*.tmp").Length);
            }
            finally
            {
                Directory.Delete(cacheFolder, recursive: true);
            }
        }

        [TestMethod]
        public void PruneCacheShouldRemoveExpiredEntries()
        {
            var cacheFolder = CreateTestCacheFolder();

            try
            {
                var expiredPath = Path.Combine(cacheFolder, "expired.html");
                File.WriteAllText(expiredPath, "expired");
                File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-31));

                SvgPreviewCacheHelper.PruneCache(cacheFolder, DateTime.UtcNow);

                Assert.IsFalse(File.Exists(expiredPath));
            }
            finally
            {
                Directory.Delete(cacheFolder, recursive: true);
            }
        }

        [TestMethod]
        public void TryWriteCacheFileAtomicShouldRejectOversizedEntry()
        {
            var cacheFolder = CreateTestCacheFolder();

            try
            {
                var cacheKey = SvgPreviewCacheHelper.BuildCacheKey("oversized");

                Assert.IsFalse(SvgPreviewCacheHelper.TryWriteCacheFileAtomic(cacheFolder, cacheKey, "1234", out var cacheFilePath, maxCacheSizeBytes: 3));
                Assert.IsFalse(File.Exists(cacheFilePath));
            }
            finally
            {
                Directory.Delete(cacheFolder, recursive: true);
            }
        }

        [TestMethod]
        public void TryWriteTransientFileShouldPersistOversizedFallback()
        {
            var userDataFolder = CreateTestCacheFolder();

            try
            {
                Assert.IsTrue(SvgPreviewCacheHelper.TryWriteTransientFile(userDataFolder, "contents", out var transientFilePath));
                Assert.AreEqual("contents", File.ReadAllText(transientFilePath));
            }
            finally
            {
                Directory.Delete(userDataFolder, recursive: true);
            }
        }

        private static string CreateTestCacheFolder()
        {
            var cacheFolder = Path.Combine(AppContext.BaseDirectory, $"SvgPreviewCacheTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(cacheFolder);
            return cacheFolder;
        }
    }
}
