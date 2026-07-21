// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml;
using System.Xml.Linq;

using Color = System.Windows.Media.Color;

namespace Microsoft.PowerToys.ThumbnailHandler.ThreeMf
{
    internal static class ThreeMfModelLoader
    {
        private static readonly string[] ThumbnailExtensions = { ".png", ".jpg", ".jpeg" };

        // Because Explorer invokes this provider on untrusted files, cap the amount of work a
        // single 3MF (a ZIP of XML) can trigger to avoid decompression/geometry bombs.
        private const long MaxUncompressedThumbnailBytes = 32L * 1024 * 1024; // 32 MB
        private const long MaxUncompressedModelBytes = 128L * 1024 * 1024; // 128 MB
        private const int MaxThumbnailDimension = 10000;
        private const int MaxTotalTriangles = 2_000_000;
        private const int MaxComponentDepth = 16;

        public static System.Drawing.Bitmap TryLoadEmbeddedThumbnail(Stream stream, uint maxSize)
        {
            if (stream == null || !stream.CanRead)
            {
                return null;
            }

            try
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var thumbnailEntry = FindThumbnailEntry(archive);
                if (thumbnailEntry == null)
                {
                    return null;
                }

                // Reject entries whose declared uncompressed size is absent or too large before
                // decompressing/decoding them.
                if (thumbnailEntry.Length <= 0 || thumbnailEntry.Length > MaxUncompressedThumbnailBytes)
                {
                    return null;
                }

                using var thumbnailStream = thumbnailEntry.Open();
                using var memoryStream = new MemoryStream();
                CopyWithLimit(thumbnailStream, memoryStream, MaxUncompressedThumbnailBytes);
                memoryStream.Position = 0;

                using var image = System.Drawing.Image.FromStream(memoryStream);
                if (image.Width <= 0 || image.Height <= 0 ||
                    image.Width > MaxThumbnailDimension || image.Height > MaxThumbnailDimension)
                {
                    return null;
                }

                return ThreeMfThumbnailProvider.ResizeImage(new System.Drawing.Bitmap(image), maxSize);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Model3DGroup LoadModel(Stream stream, Color materialColor)
        {
            if (stream == null || !stream.CanRead)
            {
                return null;
            }

            try
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

                // Resolve only the model part(s) reachable through the OPC 3D-model relationship;
                // fall back to every .model part when no relationship is declared.
                var modelEntries = ResolveModelEntries(archive);
                if (modelEntries.Count == 0)
                {
                    return null;
                }

                var modelGroup = new Model3DGroup();
                var material = new DiffuseMaterial(new SolidColorBrush(materialColor));

                var triangleBudget = MaxTotalTriangles;

                foreach (var modelEntry in modelEntries)
                {
                    if (modelEntry.Length <= 0 || modelEntry.Length > MaxUncompressedModelBytes)
                    {
                        continue;
                    }

                    using var modelStream = modelEntry.Open();
                    var document = LoadXmlSafe(modelStream);
                    AppendModelMeshes(document, modelGroup, material, ref triangleBudget);

                    if (triangleBudget <= 0)
                    {
                        break;
                    }
                }

                return modelGroup.Children.Count > 0 ? modelGroup : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ZipArchiveEntry FindThumbnailEntry(ZipArchive archive)
        {
            // 1. Prefer the thumbnail declared through the OPC relationship (authoritative).
            foreach (var target in GetThumbnailTargetsFromRelationships(archive))
            {
                var entry = ResolveEntry(archive, target);
                if (entry != null)
                {
                    return entry;
                }
            }

            // 2. Fall back to filename/location heuristics only if no relationship is declared.
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.Contains("Metadata/", StringComparison.OrdinalIgnoreCase) &&
                    ThumbnailExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    return entry;
                }

                if (name.EndsWith("thumbnail.png", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("thumbnail.jpg", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("thumbnail.jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static ZipArchiveEntry ResolveEntry(ZipArchive archive, string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            var normalized = target.Replace('\\', '/');
            return archive.GetEntry(normalized.TrimStart('/')) ??
                   archive.GetEntry(normalized) ??
                   archive.Entries.FirstOrDefault(e =>
                       e.FullName.Replace('\\', '/').EndsWith(normalized.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
        }

        private static List<ZipArchiveEntry> ResolveModelEntries(ZipArchive archive)
        {
            var resolved = new List<ZipArchiveEntry>();
            foreach (var target in GetTargetsFromRelationships(archive, "3dmodel"))
            {
                var entry = ResolveEntry(archive, target);
                if (entry != null && !resolved.Contains(entry))
                {
                    resolved.Add(entry);
                }
            }

            if (resolved.Count > 0)
            {
                return resolved;
            }

            return archive.Entries
                .Where(entry => entry.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static XDocument LoadXmlSafe(Stream stream)
        {
            // Disable DTD/entity expansion and external resolution to prevent XXE / entity-expansion
            // attacks from untrusted 3MF payloads.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
            };

            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader);
        }

        private static void CopyWithLimit(Stream source, Stream destination, long limit)
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > limit)
                {
                    throw new InvalidDataException("3MF entry exceeds the maximum allowed uncompressed size.");
                }

                destination.Write(buffer, 0, read);
            }
        }

        private static IEnumerable<string> GetThumbnailTargetsFromRelationships(ZipArchive archive)
        {
            return GetTargetsFromRelationships(archive, "thumbnail");
        }

        private static IEnumerable<string> GetTargetsFromRelationships(ZipArchive archive, string typeKeyword)
        {
            var targets = new List<string>();
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var relStream = entry.Open();
                var document = LoadXmlSafe(relStream);
                foreach (var relationship in document.Descendants().Where(element => element.Name.LocalName == "Relationship"))
                {
                    var type = relationship.Attribute("Type")?.Value ?? string.Empty;
                    if (!type.Contains(typeKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var target = relationship.Attribute("Target")?.Value;
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        targets.Add(target.Replace('\\', '/'));
                    }
                }
            }

            return targets;
        }

        private static void AppendModelMeshes(XDocument document, Model3DGroup modelGroup, Material material, ref int triangleBudget)
        {
            // Index every object by id so build items and <components> references can be resolved,
            // including objects that are composed purely from other objects.
            var objectsById = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var objectElement in document.Descendants().Where(element => element.Name.LocalName == "object"))
            {
                var id = objectElement.Attribute("id")?.Value;
                if (!string.IsNullOrWhiteSpace(id) && !objectsById.ContainsKey(id))
                {
                    objectsById[id] = objectElement;
                }
            }

            var buildItems = document.Descendants().Where(element => element.Name.LocalName == "item").ToList();
            if (buildItems.Count > 0)
            {
                foreach (var buildItem in buildItems)
                {
                    var objectId = buildItem.Attribute("objectid")?.Value;
                    var transform = ParseTransform(buildItem.Attribute("transform")?.Value) ?? Matrix3D.Identity;
                    ResolveObject(objectId, transform, objectsById, modelGroup, material, new HashSet<string>(StringComparer.Ordinal), 0, ref triangleBudget);

                    if (triangleBudget <= 0)
                    {
                        break;
                    }
                }
            }
            else
            {
                // No build section: render every object that directly contains a mesh.
                foreach (var objectId in objectsById.Keys)
                {
                    ResolveObject(objectId, Matrix3D.Identity, objectsById, modelGroup, material, new HashSet<string>(StringComparer.Ordinal), 0, ref triangleBudget);

                    if (triangleBudget <= 0)
                    {
                        break;
                    }
                }
            }
        }

        private static void ResolveObject(
            string objectId,
            Matrix3D transform,
            Dictionary<string, XElement> objectsById,
            Model3DGroup modelGroup,
            Material material,
            HashSet<string> visiting,
            int depth,
            ref int triangleBudget)
        {
            if (depth > MaxComponentDepth || triangleBudget <= 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(objectId) || !objectsById.TryGetValue(objectId, out var objectElement))
            {
                return;
            }

            // Guard against reference cycles between component objects.
            if (!visiting.Add(objectId))
            {
                return;
            }

            try
            {
                var meshElement = objectElement.Elements().FirstOrDefault(element => element.Name.LocalName == "mesh");
                if (meshElement != null)
                {
                    var geometry = CreateMeshGeometry(meshElement, ref triangleBudget);
                    if (geometry.TriangleIndices.Count > 0)
                    {
                        var transformedGeometry = transform.IsIdentity ? geometry : ApplyTransform(geometry, transform);
                        modelGroup.Children.Add(new GeometryModel3D(transformedGeometry, material));
                    }
                }

                foreach (var component in objectElement.Descendants().Where(element => element.Name.LocalName == "component"))
                {
                    if (triangleBudget <= 0)
                    {
                        break;
                    }

                    var childId = component.Attribute("objectid")?.Value;
                    var childTransform = ParseTransform(component.Attribute("transform")?.Value);

                    // Component transform is applied first, then the parent transform (row-vector convention).
                    var combined = childTransform.HasValue ? childTransform.Value * transform : transform;
                    ResolveObject(childId, combined, objectsById, modelGroup, material, visiting, depth + 1, ref triangleBudget);
                }
            }
            finally
            {
                visiting.Remove(objectId);
            }
        }

        private static MeshGeometry3D CreateMeshGeometry(XElement meshElement, ref int triangleBudget)
        {
            var vertices = meshElement.Descendants()
                .Where(element => element.Name.LocalName == "vertex")
                .Select(element => new Point3D(
                    ParseDouble(element.Attribute("x")?.Value),
                    ParseDouble(element.Attribute("y")?.Value),
                    ParseDouble(element.Attribute("z")?.Value)))
                .ToList();

            var positions = new Point3DCollection();
            var triangleIndices = new Int32Collection();

            foreach (var triangle in meshElement.Descendants().Where(element => element.Name.LocalName == "triangle"))
            {
                if (triangleBudget <= 0)
                {
                    break;
                }

                var v1 = ParseInt(triangle.Attribute("v1")?.Value);
                var v2 = ParseInt(triangle.Attribute("v2")?.Value);
                var v3 = ParseInt(triangle.Attribute("v3")?.Value);

                if (v1 < 0 || v2 < 0 || v3 < 0 ||
                    v1 >= vertices.Count || v2 >= vertices.Count || v3 >= vertices.Count)
                {
                    continue;
                }

                triangleBudget--;

                triangleIndices.Add(positions.Count);
                positions.Add(vertices[v1]);
                triangleIndices.Add(positions.Count);
                positions.Add(vertices[v2]);
                triangleIndices.Add(positions.Count);
                positions.Add(vertices[v3]);
            }

            return new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = triangleIndices,
            };
        }

        private static MeshGeometry3D ApplyTransform(MeshGeometry3D geometry, Matrix3D transform)
        {
            var transformedPositions = new Point3DCollection(geometry.Positions.Count);
            foreach (var position in geometry.Positions)
            {
                var point = transform.Transform(position);
                transformedPositions.Add(point);
            }

            return new MeshGeometry3D
            {
                Positions = transformedPositions,
                TriangleIndices = geometry.TriangleIndices,
            };
        }

        private static Matrix3D? ParseTransform(string transformValue)
        {
            if (string.IsNullOrWhiteSpace(transformValue))
            {
                return null;
            }

            var values = transformValue
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseDouble)
                .ToArray();

            if (values.Length != 12)
            {
                return null;
            }

            return new Matrix3D(
                values[0],
                values[1],
                values[2],
                0,
                values[3],
                values[4],
                values[5],
                0,
                values[6],
                values[7],
                values[8],
                0,
                values[9],
                values[10],
                values[11],
                1);
        }

        private static double ParseDouble(string value)
        {
            return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result)
                ? result
                : -1;
        }
    }
}
