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
        private static readonly char[] TransformSeparators = { ' ', '\t', '\r', '\n' };

        // Because Explorer invokes this provider on untrusted files, cap the amount of work a
        // single 3MF (a ZIP of XML) can trigger to avoid decompression/geometry bombs.
        private const long MaxUncompressedThumbnailBytes = 32L * 1024 * 1024; // 32 MB
        private const long MaxUncompressedModelBytes = 128L * 1024 * 1024; // 128 MB
        private const int MaxThumbnailDimension = 10000;

        // Bound the total decoded raster too: per-axis caps alone still allow a ~10000x10000 image
        // (~400 MB when materialized) to be decoded from a small compressed PNG. Reject anything whose
        // total pixel count would allocate more than this before it is resized.
        private const long MaxThumbnailPixels = 24L * 1024 * 1024; // ~24 megapixels (~96 MB at 32bpp)
        private const int MaxTotalTriangles = 2_000_000;
        private const int MaxTotalVertices = 4_000_000;
        private const int MaxComponentDepth = 16;
        private const int MaxModelInstances = 100_000;
        private const int MaxObjectResolutions = 100_000;

        // Bound the number of distinct .model parts loaded while resolving Production Extension
        // cross-part component references, so a package cannot force loading an unbounded number of parts.
        private const int MaxModelParts = 64;

        // Relationship (.rels) parts are small by spec; cap them so a highly compressed relationship
        // XML entry cannot consume unbounded memory before the worker timeout fires.
        private const long MaxUncompressedRelationshipBytes = 1L * 1024 * 1024; // 1 MB
        private const long MaxXmlCharacters = 64L * 1024 * 1024; // parser guard for all XML parts

        // Standardized OPC/3MF relationship type URIs. Relationship types are URI identifiers, so we
        // match them exactly (case-insensitively) rather than by substring to avoid classifying
        // unrelated vendor relationships (e.g. a "thumbnail-settings" type) as the real thumbnail/model.
        private static readonly string[] ThumbnailRelationshipTypes =
        {
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail",
        };

        private static readonly string[] ModelRelationshipTypes =
        {
            "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel",
        };

        private static readonly XNamespace ProductionNamespace =
            "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";

        // Mutable budgets shared across the whole package so a single 3MF cannot exhaust memory/CPU
        // through large vertex lists, triangle counts, or repeated component references.
        private sealed class GeometryBudget
        {
            public int Triangles { get; set; }

            public int Vertices { get; set; }

            public int Instances { get; set; }

            public int Resolutions { get; set; }

            public bool GeometryExhausted => Triangles <= 0 || Vertices <= 0 || Instances <= 0;

            public bool Exhausted => GeometryExhausted || Resolutions <= 0;
        }

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

                // Inspect the header dimensions before forcing a full raster with new Bitmap(image);
                // reject both oversized axes and an oversized total pixel count.
                using var image = System.Drawing.Image.FromStream(memoryStream);
                if (image.Width <= 0 || image.Height <= 0 ||
                    image.Width > MaxThumbnailDimension || image.Height > MaxThumbnailDimension ||
                    (long)image.Width * image.Height > MaxThumbnailPixels)
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

                var triangleBudget = new GeometryBudget
                {
                    Triangles = MaxTotalTriangles,
                    Vertices = MaxTotalVertices,
                    Instances = MaxModelInstances,
                    Resolutions = MaxObjectResolutions,
                };

                // A single package context is shared across every root model part so that Production
                // Extension components referencing objects in other .model parts (via p:path) can be
                // resolved, while a per-part cache keeps the model-part load count bounded.
                var package = new ModelPackage(archive);

                foreach (var modelEntry in modelEntries)
                {
                    var part = package.GetPart(NormalizePartName(modelEntry.FullName));
                    if (part == null)
                    {
                        continue;
                    }

                    AppendModelMeshes(package, part, modelGroup, material, triangleBudget);

                    if (triangleBudget.Exhausted)
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
            foreach (var target in GetThumbnailTargetsFromRelationships(archive))
            {
                var entry = ResolveEntry(archive, target);
                if (entry != null)
                {
                    return entry;
                }
            }

            // Compatibility fallback for packages without a thumbnail relationship: prefer files
            // actually named thumbnail* (e.g. Auxiliaries/.thumbnails/thumbnail_3mf.png), then other
            // Metadata images.
            ZipArchiveEntry namedThumbnail = null;
            ZipArchiveEntry metadataFallback = null;

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                var fileName = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;

                if (ThumbnailExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) &&
                    fileName.Contains("thumbnail", StringComparison.OrdinalIgnoreCase))
                {
                    namedThumbnail ??= entry;
                    continue;
                }

                if (metadataFallback == null &&
                    name.Contains("Metadata/", StringComparison.OrdinalIgnoreCase) &&
                    ThumbnailExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    metadataFallback = entry;
                }
            }

            if (namedThumbnail != null)
            {
                return namedThumbnail;
            }

            return metadataFallback;
        }

        private static ZipArchiveEntry ResolveEntry(ZipArchive archive, string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            // Package-level relationship targets are resolved relative to the package root. Normalize
            // the path (handle a leading '/', './' and '../' segments) and require an exact, complete
            // part-name match. A loose EndsWith would let a missing "/Metadata/thumbnail.png" target
            // silently bind to an unrelated "vendor/Metadata/thumbnail.png" part.
            var normalized = NormalizePartName(target);
            if (normalized.Length == 0)
            {
                return null;
            }

            return archive.GetEntry(normalized) ??
                   archive.Entries.FirstOrDefault(e =>
                       string.Equals(e.FullName.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizePartName(string target)
        {
            var decodedTarget = DecodeUriPath(target);
            if (decodedTarget == null)
            {
                return string.Empty;
            }

            var path = decodedTarget.Replace('\\', '/').TrimStart('/');
            var segments = new List<string>();
            foreach (var segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }

                    continue;
                }

                segments.Add(segment);
            }

            return string.Join("/", segments);
        }

        private static List<ZipArchiveEntry> ResolveModelEntries(ZipArchive archive)
        {
            var resolved = new List<ZipArchiveEntry>();
            foreach (var target in GetTargetsFromRelationships(archive, ModelRelationshipTypes))
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
                MaxCharactersInDocument = MaxXmlCharacters,
            };

            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader);
        }

        private static long CopyWithLimit(Stream source, Stream destination, long limit)
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

            return total;
        }

        private static IEnumerable<string> GetThumbnailTargetsFromRelationships(ZipArchive archive)
        {
            return GetTargetsFromRelationships(archive, ThumbnailRelationshipTypes);
        }

        private static IEnumerable<string> GetTargetsFromRelationships(ZipArchive archive, string[] relationshipTypes)
        {
            var targets = new List<string>();
            foreach (var entry in archive.Entries)
            {
                // Package thumbnail / root-model discovery must read only the package relationship part
                // (_rels/.rels). Part-level .rels are scoped to their own source part and would require
                // source-relative resolution; merging them here could select a part-level thumbnail or
                // a non-root model as if it were package-level.
                var name = entry.FullName.Replace('\\', '/');
                if (!string.Equals(name.TrimStart('/'), "_rels/.rels", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Bound relationship parts before parsing: reject an oversized declared length and
                // copy through a size-limited buffer so a compressed .rels bomb cannot exhaust memory.
                if (entry.Length > MaxUncompressedRelationshipBytes)
                {
                    continue;
                }

                XDocument document;
                using (var relStream = entry.Open())
                using (var boundedStream = new MemoryStream())
                {
                    CopyWithLimit(relStream, boundedStream, MaxUncompressedRelationshipBytes);
                    boundedStream.Position = 0;
                    document = LoadXmlSafe(boundedStream);
                }

                foreach (var relationship in document.Descendants().Where(element => element.Name.LocalName == "Relationship"))
                {
                    var type = relationship.Attribute("Type")?.Value ?? string.Empty;
                    if (!relationshipTypes.Any(known => string.Equals(type, known, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // Ignore relationships that point outside the package; those parts are not present
                    // in the archive and must never be dereferenced by the provider.
                    var targetMode = relationship.Attribute("TargetMode")?.Value;
                    if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
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

        // A lazily-populated, size- and count-bounded view over the model parts of a 3MF package,
        // used to resolve Production Extension cross-part component references.
        private sealed class ModelPart
        {
            public string Name { get; init; }

            public XNamespace CoreNamespace { get; init; }

            public XElement Root { get; init; }

            public Dictionary<string, XElement> ObjectsById { get; init; }

            public double UnitScale { get; init; }
        }

        private sealed class ModelPackage
        {
            private readonly ZipArchive _archive;
            private readonly Dictionary<string, ModelPart> _parts = new(StringComparer.OrdinalIgnoreCase);
            private long _remainingModelBytes = MaxUncompressedModelBytes;

            public ModelPackage(ZipArchive archive)
            {
                _archive = archive;
            }

            public ModelPart GetPart(string partName)
            {
                if (string.IsNullOrEmpty(partName))
                {
                    return null;
                }

                if (_parts.TryGetValue(partName, out var cached))
                {
                    return cached;
                }

                // Cache both hits and misses; both count toward the model-part budget so a package
                // cannot force loading (or repeatedly probing) an unbounded number of parts.
                if (_parts.Count >= MaxModelParts)
                {
                    return null;
                }

                ModelPart part = null;
                var entry = _archive.GetEntry(partName) ??
                            _archive.Entries.FirstOrDefault(e => string.Equals(NormalizePartName(e.FullName), partName, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    try
                    {
                        using var partStream = entry.Open();
                        using var boundedStream = new MemoryStream();
                        long copiedBytes;
                        try
                        {
                            copiedBytes = CopyWithLimit(partStream, boundedStream, _remainingModelBytes);
                        }
                        catch (InvalidDataException)
                        {
                            _remainingModelBytes = 0;
                            _parts[partName] = null;
                            return null;
                        }

                        _remainingModelBytes -= copiedBytes;
                        boundedStream.Position = 0;
                        var document = LoadXmlSafe(boundedStream);
                        var core = document.Root?.Name.Namespace;

                        var objects = new Dictionary<string, XElement>(StringComparer.Ordinal);
                        foreach (var objectElement in document.Descendants().Where(element => element.Name.LocalName == "object" && element.Name.Namespace == core))
                        {
                            var id = objectElement.Attribute("id")?.Value;
                            if (!string.IsNullOrWhiteSpace(id) && !objects.ContainsKey(id))
                            {
                                objects[id] = objectElement;
                            }
                        }

                        part = new ModelPart
                        {
                            Name = partName,
                            CoreNamespace = core,
                            Root = document.Root,
                            ObjectsById = objects,
                            UnitScale = ParseUnitScale(document.Root?.Attribute("unit")?.Value),
                        };
                    }
                    catch (Exception)
                    {
                        part = null;
                    }
                }

                _parts[partName] = part;
                return part;
            }
        }

        private static void AppendModelMeshes(ModelPackage package, ModelPart part, Model3DGroup modelGroup, Material material, GeometryBudget budget)
        {
            // Only a <build> in the model root's core namespace counts; an extension-namespace
            // <ext:build> must not suppress the no-build fallback. Its core-namespace <item> children
            // are the build items; ignore <item> elements introduced by unrelated extensions.
            var buildElement = part.Root?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "build" && element.Name.Namespace == part.CoreNamespace);
            var buildItems = buildElement?
                .Elements()
                .Where(element => element.Name.LocalName == "item" && element.Name.Namespace == buildElement.Name.Namespace)
                .ToList() ?? new List<XElement>();

            if (buildElement != null)
            {
                foreach (var buildItem in buildItems)
                {
                    var objectId = buildItem.Attribute("objectid")?.Value;
                    var transform = ParseTransform(buildItem.Attribute("transform")?.Value, part.UnitScale) ?? Matrix3D.Identity;
                    var pathValue = GetExternalPartPath(buildItem, part.CoreNamespace);
                    var targetPart = string.IsNullOrWhiteSpace(pathValue)
                        ? part
                        : package.GetPart(NormalizePartPath(part.Name, pathValue));

                    ResolveObject(package, targetPart, objectId, transform, modelGroup, material, new HashSet<string>(StringComparer.Ordinal), 0, budget);

                    if (budget.Exhausted)
                    {
                        break;
                    }
                }
            }
            else
            {
                // No build section: render every object in this part that directly contains a mesh.
                foreach (var objectEntry in part.ObjectsById)
                {
                    if (!objectEntry.Value.Elements().Any(element => element.Name.LocalName == "mesh" && element.Name.Namespace == part.CoreNamespace))
                    {
                        continue;
                    }

                    ResolveObject(package, part, objectEntry.Key, Matrix3D.Identity, modelGroup, material, new HashSet<string>(StringComparer.Ordinal), 0, budget);

                    if (budget.Exhausted)
                    {
                        break;
                    }
                }
            }
        }

        private static void ResolveObject(
            ModelPackage package,
            ModelPart part,
            string objectId,
            Matrix3D transform,
            Model3DGroup modelGroup,
            Material material,
            HashSet<string> visiting,
            int depth,
            GeometryBudget budget)
        {
            if (budget.Resolutions <= 0)
            {
                return;
            }

            budget.Resolutions--;
            if (depth > MaxComponentDepth || budget.GeometryExhausted || part == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(objectId) || !part.ObjectsById.TryGetValue(objectId, out var objectElement))
            {
                return;
            }

            // Guard against reference cycles between component objects, keyed by part + id so the same
            // object id in different parts is not conflated.
            var visitKey = part.Name + "\0" + objectId;
            if (!visiting.Add(visitKey))
            {
                return;
            }

            try
            {
                var meshElement = objectElement.Elements().FirstOrDefault(element => element.Name.LocalName == "mesh" && element.Name.Namespace == part.CoreNamespace);
                if (meshElement != null)
                {
                    var geometry = CreateMeshGeometry(meshElement, budget, part.UnitScale);
                    if (geometry.TriangleIndices.Count > 0 && budget.Instances > 0)
                    {
                        budget.Instances--;
                        var transformedGeometry = transform.IsIdentity ? geometry : ApplyTransform(geometry, transform);
                        modelGroup.Children.Add(new GeometryModel3D(transformedGeometry, material));
                    }
                }

                foreach (var component in objectElement.Descendants().Where(element => element.Name.LocalName == "component" && element.Name.Namespace == part.CoreNamespace))
                {
                    if (budget.Exhausted)
                    {
                        break;
                    }

                    var childId = component.Attribute("objectid")?.Value;
                    var childTransform = ParseTransform(component.Attribute("transform")?.Value, part.UnitScale);

                    // Component transform is applied first, then the parent transform (row-vector convention).
                    var combined = childTransform.HasValue ? childTransform.Value * transform : transform;

                    // 3MF Production Extension: a component may carry a p:path attribute in the
                    // standardized Production namespace referencing an object in another .model part.
                    // Resolve into that part; otherwise the reference is same-part.
                    var pathValue = GetExternalPartPath(component, part.CoreNamespace);

                    var targetPart = string.IsNullOrWhiteSpace(pathValue)
                        ? part
                        : package.GetPart(NormalizePartPath(part.Name, pathValue));

                    ResolveObject(package, targetPart, childId, combined, modelGroup, material, visiting, depth + 1, budget);
                }
            }
            finally
            {
                visiting.Remove(visitKey);
            }
        }

        private static string GetExternalPartPath(XElement element, XNamespace coreNamespace)
        {
            return element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "path" &&
                                             attribute.Name.Namespace == ProductionNamespace)?.Value;
        }

        private static MeshGeometry3D CreateMeshGeometry(XElement meshElement, GeometryBudget budget, double unitScale)
        {
            // Vertices/triangles must share the mesh's (core) namespace; extension-namespace elements
            // with the same local name are not valid core mesh geometry and are ignored.
            var meshNamespace = meshElement.Name.Namespace;

            // Materialize vertices under the shared vertex budget so a mesh with a huge vertex list
            // (even with few/no triangles) cannot cause an unbounded allocation.
            var vertices = new List<Point3D>();
            foreach (var element in meshElement.Descendants().Where(e => e.Name.LocalName == "vertex" && e.Name.Namespace == meshNamespace))
            {
                if (budget.Vertices <= 0)
                {
                    break;
                }

                budget.Vertices--;
                vertices.Add(new Point3D(
                    ParseDouble(element.Attribute("x")?.Value) * unitScale,
                    ParseDouble(element.Attribute("y")?.Value) * unitScale,
                    ParseDouble(element.Attribute("z")?.Value) * unitScale));
            }

            var positions = new Point3DCollection(vertices.Count);
            foreach (var vertex in vertices)
            {
                positions.Add(vertex);
            }

            var triangleIndices = new Int32Collection();

            foreach (var triangle in meshElement.Descendants().Where(element => element.Name.LocalName == "triangle" && element.Name.Namespace == meshNamespace))
            {
                if (budget.Triangles <= 0)
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

                budget.Triangles--;

                triangleIndices.Add(v1);
                triangleIndices.Add(v2);
                triangleIndices.Add(v3);
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

        private static Matrix3D? ParseTransform(string transformValue, double unitScale)
        {
            if (string.IsNullOrWhiteSpace(transformValue))
            {
                return null;
            }

            var values = transformValue
                .Split(TransformSeparators, StringSplitOptions.RemoveEmptyEntries)
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
                values[9] * unitScale,
                values[10] * unitScale,
                values[11] * unitScale,
                1);
        }

        private static string NormalizePartPath(string basePartPath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return null;
            }

            var normalizedBasePath = basePartPath.Replace('\\', '/');
            var decodedTarget = DecodeUriPath(targetPath);
            if (decodedTarget == null)
            {
                return null;
            }

            var normalizedTargetPath = decodedTarget.Replace('\\', '/');
            var lastSeparator = normalizedBasePath.LastIndexOf('/');
            var combinedPath = normalizedTargetPath.StartsWith('/')
                ? normalizedTargetPath.TrimStart('/')
                : (lastSeparator >= 0 ? normalizedBasePath[..(lastSeparator + 1)] : string.Empty) + normalizedTargetPath;

            var segments = new List<string>();
            foreach (var segment in combinedPath.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        return null;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            var normalizedPath = string.Join("/", segments);
            return normalizedPath.EndsWith(".model", StringComparison.OrdinalIgnoreCase) ? normalizedPath : null;
        }

        private static string DecodeUriPath(string path)
        {
            try
            {
                return Uri.UnescapeDataString(path);
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        private static double ParseUnitScale(string unit)
        {
            return unit?.ToLowerInvariant() switch
            {
                "micron" => 0.001,
                "centimeter" => 10,
                "inch" => 25.4,
                "foot" => 304.8,
                "meter" => 1000,
                _ => 1,
            };
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
