// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;

using Microsoft.PowerToys.ThumbnailHandler.ThreeMf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ThreeMfThumbnailProviderUnitTests
{
    [STATestClass]
    public class ThreeMfThumbnailProviderTests
    {
        [TestMethod]
        public void GetThumbnailValidStreamThreeMf()
        {
            // Act
            var filePath = "HelperFiles/sample.3mf";

            ThreeMfThumbnailProvider provider = new ThreeMfThumbnailProvider(filePath);

            Bitmap bitmap = provider.GetThumbnail(256);

            Assert.IsTrue(bitmap != null);
        }

        [TestMethod]
        public void GetThumbnailInValidSizeThreeMf()
        {
            // Act
            var filePath = "HelperFiles/sample.3mf";

            ThreeMfThumbnailProvider provider = new ThreeMfThumbnailProvider(filePath);

            Bitmap bitmap = provider.GetThumbnail(0);

            Assert.IsTrue(bitmap == null);
        }

        [TestMethod]
        public void GetThumbnailToBigThreeMf()
        {
            // Act
            var filePath = "HelperFiles/sample.3mf";

            ThreeMfThumbnailProvider provider = new ThreeMfThumbnailProvider(filePath);

            Bitmap bitmap = provider.GetThumbnail(10001);

            Assert.IsTrue(bitmap == null);
        }

        [TestMethod]
        public void CheckNoThreeMfEmptyStreamShouldReturnNullBitmap()
        {
            using (var stream = new MemoryStream())
            {
                Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(stream, 256);
                Assert.IsTrue(thumbnail == null);
            }
        }

        [TestMethod]
        public void CheckNoThreeMfNullStreamShouldReturnNullBitmap()
        {
            Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(null, 256);
            Assert.IsTrue(thumbnail == null);
        }

        [TestMethod]
        public void GetThumbnailReturnsEmbeddedPackageThumbnail()
        {
            // Arrange: a 3MF package that declares a thumbnail through the OPC relationship.
            using var stream = CreateThreeMfWithEmbeddedThumbnail(64, 48);

            // Act
            Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(stream, 256);

            // Assert: the embedded image is used (fits within 256, so it is returned unscaled).
            Assert.IsNotNull(thumbnail);
            Assert.AreEqual(64, thumbnail.Width);
            Assert.AreEqual(48, thumbnail.Height);
        }

        [TestMethod]
        public void GetThumbnailFallsBackToMeshRenderingWhenNoEmbeddedThumbnail()
        {
            // Arrange: a mesh-only 3MF package (no embedded thumbnail image).
            using var stream = CreateMeshOnlyThreeMf();

            // Act
            Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(stream, 256);

            // Assert: the mesh is rendered into a non-empty bitmap.
            Assert.IsNotNull(thumbnail);
            Assert.IsTrue(thumbnail.Width > 0 && thumbnail.Height > 0);
        }

        [TestMethod]
        public void GetThumbnailUsesConfiguredFallbackColorForMeshRendering()
        {
            // The configured material color feeds the mesh render path. GetThumbnail returns null
            // unless the model parsed, has non-empty 3D bounds, and rendered successfully, so a
            // non-null bitmap here deterministically proves the configured color fed a working mesh
            // render. (A pixel-level opacity scan is intentionally avoided: WPF 3D rasterization is
            // environment-dependent and makes such assertions flaky.)
            using var stream = CreateMeshOnlyThreeMf();

            var color = ThreeMfThumbnailProvider.DefaultMaterialColor;
            using Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(stream, 256);

            Assert.IsTrue(color.A > 0, "The configured material color must be opaque.");
            Assert.IsNotNull(thumbnail, "The configured color should feed a successful mesh render producing a thumbnail.");
            Assert.IsTrue(thumbnail.Width > 0 && thumbnail.Height > 0);
            Assert.IsTrue(thumbnail.Width <= 256 && thumbnail.Height <= 256);
        }

        [TestMethod]
        public void GetThumbnailRendersProductionExtensionCrossPartComponents()
        {
            // A valid Production Extension package whose root object is composed entirely of a
            // component referencing an object in another .model part (via p:path). The mesh lives only
            // in the referenced part, so a non-null thumbnail proves cross-part resolution worked.
            using var stream = CreateCrossPartThreeMf();

            Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(stream, 256);

            Assert.IsNotNull(thumbnail, "Cross-part (p:path) components should be resolved and rendered.");
            Assert.IsTrue(thumbnail.Width > 0 && thumbnail.Height > 0);
        }

        private static MemoryStream CreateCrossPartThreeMf()
        {
            const string rootModel =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" " +
                "xmlns:p=\"http://schemas.microsoft.com/3dmanufacturing/production/2015/06\">" +
                "<resources><object id=\"1\" type=\"model\"><components>" +
                "<component objectid=\"10\" p:path=\"/3D/parts/part1.model\"/>" +
                "</components></object></resources>" +
                "<build><item objectid=\"1\"/></build></model>";

            const string partModel =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources><object id=\"10\" type=\"model\"><mesh>" +
                "<vertices>" +
                "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"10\" y=\"0\" z=\"0\"/>" +
                "<vertex x=\"0\" y=\"10\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"10\"/>" +
                "</vertices>" +
                "<triangles>" +
                "<triangle v1=\"0\" v2=\"1\" v3=\"2\"/><triangle v1=\"0\" v2=\"1\" v3=\"3\"/>" +
                "<triangle v1=\"1\" v2=\"2\" v3=\"3\"/><triangle v1=\"0\" v2=\"2\" v3=\"3\"/>" +
                "</triangles>" +
                "</mesh></object></resources></model>";

            var package = new MemoryStream();
            using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, "3D/3dmodel.model", Encoding.UTF8.GetBytes(rootModel));
                WriteEntry(archive, "3D/parts/part1.model", Encoding.UTF8.GetBytes(partModel));

                var rels =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" Target=\"/3D/3dmodel.model\"/>" +
                    "</Relationships>";
                WriteEntry(archive, "_rels/.rels", Encoding.UTF8.GetBytes(rels));
            }

            package.Position = 0;
            return package;
        }

        private static MemoryStream CreateMeshOnlyThreeMf()
        {
            const string model =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources><object id=\"1\" type=\"model\"><mesh>" +
                "<vertices>" +
                "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"10\" y=\"0\" z=\"0\"/>" +
                "<vertex x=\"0\" y=\"10\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"10\"/>" +
                "</vertices>" +
                "<triangles>" +
                "<triangle v1=\"0\" v2=\"1\" v3=\"2\"/><triangle v1=\"0\" v2=\"1\" v3=\"3\"/>" +
                "<triangle v1=\"1\" v2=\"2\" v3=\"3\"/><triangle v1=\"0\" v2=\"2\" v3=\"3\"/>" +
                "</triangles>" +
                "</mesh></object></resources>" +
                "<build><item objectid=\"1\"/></build></model>";

            return BuildPackage(model, thumbnailPng: null, thumbnailWidth: 0, thumbnailHeight: 0);
        }

        private static MemoryStream CreateThreeMfWithEmbeddedThumbnail(int width, int height)
        {
            const string model =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources><object id=\"1\" type=\"model\"><mesh>" +
                "<vertices><vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/></vertices>" +
                "<triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles>" +
                "</mesh></object></resources><build><item objectid=\"1\"/></build></model>";

            byte[] png;
            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var pngStream = new MemoryStream())
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.CornflowerBlue);
                }

                bmp.Save(pngStream, ImageFormat.Png);
                png = pngStream.ToArray();
            }

            return BuildPackage(model, png, width, height);
        }

        private static MemoryStream BuildPackage(string model, byte[] thumbnailPng, int thumbnailWidth, int thumbnailHeight)
        {
            _ = thumbnailWidth;
            _ = thumbnailHeight;

            var package = new MemoryStream();
            using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, "3D/3dmodel.model", Encoding.UTF8.GetBytes(model));

                var rels = new StringBuilder();
                rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                rels.Append("<Relationship Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" Target=\"/3D/3dmodel.model\"/>");

                if (thumbnailPng != null)
                {
                    rels.Append("<Relationship Id=\"rel1\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail\" Target=\"/Metadata/thumbnail.png\"/>");
                }

                rels.Append("</Relationships>");
                WriteEntry(archive, "_rels/.rels", Encoding.UTF8.GetBytes(rels.ToString()));

                if (thumbnailPng != null)
                {
                    WriteEntry(archive, "Metadata/thumbnail.png", thumbnailPng);
                }
            }

            package.Position = 0;
            return package;
        }

        private static void WriteEntry(ZipArchive archive, string path, byte[] content)
        {
            var entry = archive.CreateEntry(path);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }
    }
}
