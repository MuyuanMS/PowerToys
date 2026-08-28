// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.ThumbnailHandler.ThreeMf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

using GeometryModel3D = System.Windows.Media.Media3D.GeometryModel3D;
using MediaColors = System.Windows.Media.Colors;
using MeshGeometry3D = System.Windows.Media.Media3D.MeshGeometry3D;
using Model3DGroup = System.Windows.Media.Media3D.Model3DGroup;

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
        public void GetThumbnailInvalidSizeThreeMf()
        {
            // Act
            var filePath = "HelperFiles/sample.3mf";

            ThreeMfThumbnailProvider provider = new ThreeMfThumbnailProvider(filePath);

            Bitmap bitmap = provider.GetThumbnail(0);

            Assert.IsTrue(bitmap == null);
        }

        [TestMethod]
        public void GetThumbnailTooBigThreeMf()
        {
            // Act
            var filePath = "HelperFiles/sample.3mf";

            ThreeMfThumbnailProvider provider = new ThreeMfThumbnailProvider(filePath);

            Bitmap bitmap = provider.GetThumbnail(10001);

            Assert.IsTrue(bitmap == null);
        }

        [TestMethod]
        public void ResizeImageDoesNotEnlargeFittingNonArgbBitmap()
        {
            using var source = new Bitmap(32, 24, PixelFormat.Format24bppRgb);

            using Bitmap resized = ThreeMfThumbnailProvider.ResizeImage(source, 256);

            Assert.IsNotNull(resized);
            Assert.AreEqual(32, resized.Width);
            Assert.AreEqual(24, resized.Height);
            Assert.AreEqual(PixelFormat.Format32bppArgb, resized.PixelFormat);
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
        public void GetThumbnailPrefersRelationshipThumbnailOverNamedFallback()
        {
            const string model =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources><object id=\"1\" type=\"model\"><mesh>" +
                "<vertices><vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/></vertices>" +
                "<triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles>" +
                "</mesh></object></resources><build><item objectid=\"1\"/></build></model>";
            using var stream = BuildPackage(
                model,
                CreatePng(64, 48, System.Drawing.Color.CornflowerBlue),
                64,
                48,
                heuristicThumbnailPng: CreatePng(16, 12, System.Drawing.Color.Red));

            using Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(stream, 256);

            Assert.IsNotNull(thumbnail);
            Assert.AreEqual(64, thumbnail.Width);
            Assert.AreEqual(48, thumbnail.Height);
        }

        [TestMethod]
        public void GetThumbnailPathChangesOnlyTerminalExtension()
        {
            const string input = @"C:\profile.3mf\model.3mf";

            string output = Program.GetThumbnailPath(input);

            Assert.AreEqual(@"C:\profile.3mf\model.png", output);
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
            using var stream = CreateMeshOnlyThreeMf();
            var expectedColor = System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56);
            var configuredSettings = new PowerPreviewSettings();
            configuredSettings.Properties.ThreeMfThumbnailColor.Value = "#123456";
            var settingsUtils = new Mock<SettingsUtils>(new System.IO.Abstractions.FileSystem(), null);
            settingsUtils
                .Setup(utils => utils.GetSettings<PowerPreviewSettings>(PowerPreviewSettings.ModuleName, SettingsUtils.DefaultFileName))
                .Returns(configuredSettings);
            var materialColor = ThreeMfThumbnailProvider.GetMaterialColor(settingsUtils.Object);

            var loaderType = typeof(ThreeMfThumbnailProvider).Assembly.GetType("Microsoft.PowerToys.ThumbnailHandler.ThreeMf.ThreeMfModelLoader");
            var loadModel = loaderType.GetMethod("LoadModel", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            var model = loadModel.Invoke(null, new object[] { stream, materialColor }) as System.Windows.Media.Media3D.Model3DGroup;
            Assert.IsNotNull(model);

            var geometry = model.Children[0] as System.Windows.Media.Media3D.GeometryModel3D;
            Assert.IsNotNull(geometry);

            var material = geometry.Material as System.Windows.Media.Media3D.DiffuseMaterial;
            Assert.IsNotNull(material);

            var brush = material.Brush as System.Windows.Media.SolidColorBrush;
            Assert.IsNotNull(brush);
            Assert.AreEqual(expectedColor, brush.Color);
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

        [TestMethod]
        public void GetThumbnailRendersProductionExtensionCrossPartBuildItem()
        {
            using var stream = CreateCrossPartThreeMf(directBuildReference: true);

            Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(stream, 256);

            Assert.IsNotNull(thumbnail, "A build item with p:path should resolve its object from the referenced model part.");
            Assert.IsTrue(thumbnail.Width > 0 && thumbnail.Height > 0);
        }

        [TestMethod]
        public void LoadModelResolvesEscapedRelationshipTarget()
        {
            const string model =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources><object id=\"1\" type=\"model\"><mesh>" +
                "<vertices><vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/></vertices>" +
                "<triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles>" +
                "</mesh></object></resources><build><item objectid=\"1\"/></build></model>";
            using var stream = BuildPackage(model, thumbnailPng: null, thumbnailWidth: 0, thumbnailHeight: 0, modelPath: "3D/My Model.model", relationshipTarget: "/3D/My%20Model.model");

            Model3DGroup loaded = ThreeMfModelLoader.LoadModel(stream, MediaColors.Gold);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Children.Count);
        }

        [TestMethod]
        public void LoadModelNormalizesCrossPartUnits()
        {
            using var stream = CreateCrossPartThreeMf(directBuildReference: true, partUnit: "inch", vertexExtent: 1);

            Model3DGroup model = ThreeMfModelLoader.LoadModel(stream, MediaColors.Gold);

            Assert.IsNotNull(model);
            var geometry = model.Children.OfType<GeometryModel3D>().Single();
            Assert.AreEqual(25.4, geometry.Bounds.SizeX, 0.001);
            Assert.AreEqual(25.4, geometry.Bounds.SizeY, 0.001);
            Assert.AreEqual(25.4, geometry.Bounds.SizeZ, 0.001);
        }

        [TestMethod]
        public void LoadModelAppliesWhitespaceSeparatedNestedTransforms()
        {
            using var stream = CreateCrossPartThreeMf(
                componentTransform: "1 0 0 0\t1 0 0 0 1 1 0 0",
                buildTransform: "1 0 0 0 1 0 0 0 1 0\n2 0",
                vertexExtent: 1);

            Model3DGroup model = ThreeMfModelLoader.LoadModel(stream, MediaColors.Gold);

            Assert.IsNotNull(model);
            var geometry = model.Children.OfType<GeometryModel3D>().Single();
            Assert.AreEqual(1, geometry.Bounds.X, 0.001);
            Assert.AreEqual(2, geometry.Bounds.Y, 0.001);
        }

        [TestMethod]
        public void LoadModelPreservesIndexedMeshVertices()
        {
            using var stream = CreateMeshOnlyThreeMf();

            Model3DGroup model = ThreeMfModelLoader.LoadModel(stream, MediaColors.Gold);

            Assert.IsNotNull(model);
            var geometry = model.Children.OfType<GeometryModel3D>().Single().Geometry as MeshGeometry3D;
            Assert.IsNotNull(geometry);
            Assert.AreEqual(4, geometry.Positions.Count);
            Assert.AreEqual(12, geometry.TriangleIndices.Count);
        }

        [TestMethod]
        public void LoadModelHonorsExplicitEmptyBuild()
        {
            const string model =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources><object id=\"1\" type=\"model\"><mesh>" +
                "<vertices><vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/></vertices>" +
                "<triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles>" +
                "</mesh></object></resources><build/></model>";
            using var stream = BuildPackage(model, thumbnailPng: null, thumbnailWidth: 0, thumbnailHeight: 0);

            Model3DGroup loaded = ThreeMfModelLoader.LoadModel(stream, MediaColors.Gold);

            Assert.IsNull(loaded, "An explicit empty build must not instantiate resource objects.");
        }

        [TestMethod]
        public void LoadModelNoBuildFallbackRendersOnlyDirectMeshes()
        {
            const string model =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources>" +
                "<object id=\"1\" type=\"model\"><mesh>" +
                "<vertices><vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/></vertices>" +
                "<triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles>" +
                "</mesh></object>" +
                "<object id=\"2\" type=\"model\"><components><component objectid=\"1\"/></components></object>" +
                "</resources></model>";
            using var stream = BuildPackage(model, thumbnailPng: null, thumbnailWidth: 0, thumbnailHeight: 0);

            Model3DGroup loaded = ThreeMfModelLoader.LoadModel(stream, MediaColors.Gold);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Children.Count, "Component-only resource objects must not duplicate their child meshes in the no-build fallback.");
        }

        [TestMethod]
        public void GetThumbnailSupportsNonSeekableMeshStream()
        {
            using var package = CreateMeshOnlyThreeMf();
            using var stream = new NonSeekableReadStream(package);

            using Bitmap thumbnail = ThreeMfThumbnailProvider.GetThumbnail(stream, 256);

            Assert.IsNotNull(thumbnail);
        }

        private static MemoryStream CreateCrossPartThreeMf(
            bool directBuildReference = false,
            string partUnit = "millimeter",
            int vertexExtent = 10,
            string componentTransform = null,
            string buildTransform = null)
        {
            var componentTransformAttribute = componentTransform is null ? string.Empty : $" transform=\"{componentTransform}\"";
            var buildTransformAttribute = buildTransform is null ? string.Empty : $" transform=\"{buildTransform}\"";
            var rootModel =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" " +
                "xmlns:p=\"http://schemas.microsoft.com/3dmanufacturing/production/2015/06\">" +
                (directBuildReference
                    ? $"<resources/><build><item objectid=\"10\" p:path=\"/3D/parts/part1.model\"{buildTransformAttribute}/></build></model>"
                    : "<resources><object id=\"1\" type=\"model\"><components>" +
                      $"<component objectid=\"10\" p:path=\"/3D/parts/part1.model\"{componentTransformAttribute}/>" +
                      $"</components></object></resources><build><item objectid=\"1\"{buildTransformAttribute}/></build></model>");

            var partModel =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                $"<model unit=\"{partUnit}\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources><object id=\"10\" type=\"model\"><mesh>" +
                "<vertices>" +
                $"<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"{vertexExtent}\" y=\"0\" z=\"0\"/>" +
                $"<vertex x=\"0\" y=\"{vertexExtent}\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"{vertexExtent}\"/>" +
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

            return BuildPackage(model, CreatePng(width, height, System.Drawing.Color.CornflowerBlue), width, height);
        }

        private static MemoryStream BuildPackage(
            string model,
            byte[] thumbnailPng,
            int thumbnailWidth,
            int thumbnailHeight,
            string modelPath = "3D/3dmodel.model",
            string relationshipTarget = "/3D/3dmodel.model",
            byte[] heuristicThumbnailPng = null)
        {
            _ = thumbnailWidth;
            _ = thumbnailHeight;

            var package = new MemoryStream();
            using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, modelPath, Encoding.UTF8.GetBytes(model));

                var rels = new StringBuilder();
                rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                rels.Append("<Relationship Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" Target=\"");
                rels.Append(relationshipTarget);
                rels.Append("\"/>");

                if (thumbnailPng != null)
                {
                    rels.Append("<Relationship Id=\"rel1\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail\" Target=\"/Auxiliaries/preview.png\"/>");
                }

                rels.Append("</Relationships>");
                WriteEntry(archive, "_rels/.rels", Encoding.UTF8.GetBytes(rels.ToString()));

                if (thumbnailPng != null)
                {
                    WriteEntry(archive, "Auxiliaries/preview.png", thumbnailPng);
                }

                if (heuristicThumbnailPng != null)
                {
                    WriteEntry(archive, "Metadata/thumbnail.png", heuristicThumbnailPng);
                }
            }

            package.Position = 0;
            return package;
        }

        private static byte[] CreatePng(int width, int height, System.Drawing.Color color)
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }

        private static void WriteEntry(ZipArchive archive, string path, byte[] content)
        {
            var entry = archive.CreateEntry(path);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }

        private sealed class NonSeekableReadStream : Stream
        {
            private readonly Stream innerStream;

            public NonSeekableReadStream(Stream innerStream)
            {
                this.innerStream = innerStream;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return innerStream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
