// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Peek.Common.Models;
using Peek.FilePreviewer.Models;
using Peek.FilePreviewer.Previewers;

namespace Peek.FilePreviewer.UnitTests
{
    [TestClass]
    public class WebBrowserPreviewerTests
    {
        [TestMethod]
        public void IsItemSupported_MarkdownExtension_ShouldReturnTrue()
        {
            var item = new FileItem(@"C:\some\file.md", "file.md");

            Assert.IsTrue(WebBrowserPreviewer.IsItemSupported(item));
        }

        [TestMethod]
        public void IsItemSupported_UnrecognizedExtension_ShouldReturnFalse()
        {
            var item = new FileItem(@"C:\some\file.zzzzunknown", "file.zzzzunknown");

            Assert.IsFalse(WebBrowserPreviewer.IsItemSupported(item));
        }

        [TestMethod]
        public void IsFallbackCandidate_UnrecognizedExtension_ShouldReturnTrue()
        {
            var item = new FileItem(@"C:\some\file.zzzzunknown", "file.zzzzunknown");

            Assert.IsTrue(WebBrowserPreviewer.IsFallbackCandidate(item));
        }

        // IsItemSupported should short-circuit the fallback for extensions Peek already recognizes.
        [TestMethod]
        public void IsFallbackCandidate_AlreadySupportedExtension_ShouldReturnFalse()
        {
            var item = new FileItem(@"C:\some\file.md", "file.md");

            Assert.IsFalse(WebBrowserPreviewer.IsFallbackCandidate(item));
        }

        [TestMethod]
        public void IsFallbackCandidate_FolderItem_ShouldReturnFalse()
        {
            var item = new FolderItem(@"C:\some\folder", "folder", "folder");

            Assert.IsFalse(WebBrowserPreviewer.IsFallbackCandidate(item));
        }

        [TestMethod]
        public void IsFallbackCandidate_MissingFile_ShouldReturnTrue()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".zzzzunknown");
            var item = new FileItem(missingPath, Path.GetFileName(missingPath));

            Assert.IsTrue(WebBrowserPreviewer.IsFallbackCandidate(item));
        }

        [TestMethod]
        public async Task CreatePreviewAsync_UnknownTextFile_ShouldCreateMonacoPreview()
        {
            string path = CreateTempFile(Encoding.UTF8.GetBytes("plain text"), ".zzzzunknown");

            try
            {
                var previewer = CreateFallbackPreviewer(path, maxFileSizeBytes: 1024);
                var preview = await previewer.CreatePreviewAsync(CancellationToken.None);

                Assert.IsTrue(preview.IsDevFilePreview);
                Assert.IsTrue(preview.CustomContextMenu);
                Assert.IsTrue(File.Exists(preview.PreviewUri.LocalPath));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task CreatePreviewAsync_UnknownBinaryFile_ShouldFailForInfoFallback()
        {
            string path = CreateTempFile([0x00, 0x01, 0x02], ".zzzzunknown");

            try
            {
                var previewer = CreateFallbackPreviewer(path, maxFileSizeBytes: 1024);

                await Assert.ThrowsExceptionAsync<NotSupportedException>(
                    () => previewer.CreatePreviewAsync(CancellationToken.None));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task CreatePreviewAsync_OversizedUnknownTextFile_ShouldFailForInfoFallback()
        {
            string path = CreateTempFile(Encoding.UTF8.GetBytes("too large"), ".zzzzunknown");

            try
            {
                var previewer = CreateFallbackPreviewer(path, maxFileSizeBytes: 4);

                await Assert.ThrowsExceptionAsync<NotSupportedException>(
                    () => previewer.CreatePreviewAsync(CancellationToken.None));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static WebBrowserPreviewer CreateFallbackPreviewer(string path, long maxFileSizeBytes)
        {
            var item = new FileItem(path, Path.GetFileName(path));
            var settings = new TestPreviewSettings(maxFileSizeBytes);
            var factory = new PreviewerFactory(settings, useCurrentDispatcher: false);
            var previewer = factory.Create(item);

            Assert.IsInstanceOfType(previewer, typeof(WebBrowserPreviewer));
            return (WebBrowserPreviewer)previewer;
        }

        private static string CreateTempFile(byte[] content, string extension)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + extension);
            File.WriteAllBytes(path, content);
            return path;
        }

        private sealed class TestPreviewSettings(long maxFileSizeBytes) : IPreviewSettings
        {
            public bool SourceCodeWrapText => false;

            public bool SourceCodeTryFormat => false;

            public int SourceCodeFontSize => 14;

            public bool SourceCodeStickyScroll => true;

            public bool SourceCodeMinimap => false;

            public long SourceCodeMaxFileSizeBytes => maxFileSizeBytes;
        }
    }
}
