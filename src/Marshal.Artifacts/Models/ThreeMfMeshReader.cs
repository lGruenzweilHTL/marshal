using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Xml;
using Marshal.Abstractions;

namespace Marshal.Artifacts.Models;

/// <summary>
/// 3MF: an OPC zip whose root model part (named in <c>_rels/.rels</c>) lists mesh objects, component objects and
/// build items. Reads the core spec, base materials and color groups for colors, and production-extension components
/// (<c>p:path</c>) that point into other model parts, as slicers write them. Units are converted to millimetres.
/// Bounded for hostile files: entry count, uncompressed bytes, triangles, component depth and cycles, no DTDs.
/// </summary>
internal sealed class ThreeMfMeshReader(MeshLimits? limits = null) : IMeshReader
{
    private const string DefaultRoot = "3D/3dmodel.model";

    private readonly MeshLimits _limits = limits ?? MeshLimits.Default;

    public Result<Mesh> Read(Stream content, CancellationToken cancellationToken)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > _limits.MaxEntries)
                return MeshErrors.Unreadable($"the package has more than {_limits.MaxEntries.ToString(CultureInfo.InvariantCulture)} entries");

            var package = new Package(zip, _limits, cancellationToken);
            string rootPath = package.RootPath();
            Part root = package.Load(rootPath);
            if (root.Build.Count == 0)
                return MeshErrors.Unreadable("it has no build items");

            // The root part's unit applies once, after every build and component transform.
            var unit = Matrix4x4.CreateScale(root.Scale);
            var builder = new Builder(package, _limits);
            foreach ((int objectId, Matrix4x4 transform) in root.Build)
                builder.Emit(rootPath, objectId, transform * unit, depth: 0);
            return builder.Triangles.Count == 0
                ? MeshErrors.Unreadable("it has no triangles")
                : new Mesh([.. builder.Triangles], builder.HasColors ? [.. builder.Colors] : null);
        }
        catch (ModelException failure)
        {
            return MeshErrors.Unreadable(failure.Message);
        }
        catch (Exception failure) when (failure is InvalidDataException or XmlException or IOException or FormatException or OverflowException)
        {
            return MeshErrors.Unreadable($"the 3MF package is damaged ({failure.Message.TrimEnd('.')})");
        }
    }

    /// <summary>The zip, its parsed model parts, and the running uncompressed byte count.</summary>
    private sealed class Package(ZipArchive zip, MeshLimits limits, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, Part> _parts = new(StringComparer.OrdinalIgnoreCase);
        private long _uncompressed;

        public CancellationToken Cancellation => cancellationToken;

        public string RootPath()
        {
            if (Entry("_rels/.rels") is not { } rels)
                return DefaultRoot;

            using XmlReader reader = Xml(rels);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship"
                    && reader.GetAttribute("Type")?.EndsWith("/3dmodel", StringComparison.Ordinal) == true
                    && reader.GetAttribute("Target") is { Length: > 0 } target)
                {
                    return Normalize(target);
                }
            }

            return DefaultRoot;
        }

        public Part Load(string path)
        {
            if (_parts.TryGetValue(path, out Part? part))
                return part;
            ZipArchiveEntry entry = Entry(path) ?? throw new ModelException($"the model part '{path}' is missing");
            part = Part.Parse(Xml(entry), cancellationToken);
            _parts[path] = part;
            return part;
        }

        private ZipArchiveEntry? Entry(string path) =>
            zip.Entries.FirstOrDefault(entry => string.Equals(entry.FullName.TrimStart('/'), path, StringComparison.OrdinalIgnoreCase));

        private XmlReader Xml(ZipArchiveEntry entry)
        {
            if (entry.Length > limits.MaxUncompressedBytes - _uncompressed)
                throw new ModelException("the package is too large when unpacked");

            var counted = new CountingStream(entry.Open(), this);
            return XmlReader.Create(counted, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CloseInput = true,
            });
        }

        public void Count(int bytes)
        {
            _uncompressed += bytes;
            if (_uncompressed > limits.MaxUncompressedBytes)
                throw new ModelException("the package is too large when unpacked");
        }
    }

    /// <summary>One model part: unit, color groups, objects and build items.</summary>
    private sealed class Part
    {
        public float Scale { get; private set; } = 1;

        public Dictionary<int, uint[]> ColorGroups { get; } = [];

        public Dictionary<int, ModelObject> Objects { get; } = [];

        public List<(int ObjectId, Matrix4x4 Transform)> Build { get; } = [];

        public static Part Parse(XmlReader reader, CancellationToken cancellationToken)
        {
            using (reader)
            {
                var part = new Part();
                ModelObject? current = null;
                List<uint>? group = null;
                int groupId = 0;
                List<Vector3>? vertices = null;
                int elements = 0;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        switch (reader.LocalName)
                        {
                            case "basematerials" or "colorgroup" when group is not null:
                                part.ColorGroups[groupId] = [.. group];
                                group = null;
                                break;
                            case "object" when current is not null:
                                part.Objects[current.Id] = current;
                                current = null;
                                vertices = null;
                                break;
                        }

                        continue;
                    }

                    if (reader.NodeType != XmlNodeType.Element)
                        continue;
                    if ((++elements & 0xFFFF) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    switch (reader.LocalName)
                    {
                        case "model":
                            part.Scale = Unit(reader.GetAttribute("unit"));
                            break;
                        case "basematerials" or "colorgroup":
                            groupId = Int(reader, "id");
                            group = [];
                            if (reader.IsEmptyElement)
                            {
                                part.ColorGroups[groupId] = [];
                                group = null;
                            }

                            break;
                        case "base" when group is not null:
                            group.Add(Color(reader.GetAttribute("displaycolor")));
                            break;
                        case "color" when group is not null:
                            group.Add(Color(reader.GetAttribute("color")));
                            break;
                        case "object":
                            current = new ModelObject(Int(reader, "id"), OptionalInt(reader, "pid"), OptionalInt(reader, "pindex") ?? 0);
                            if (reader.IsEmptyElement)
                            {
                                part.Objects[current.Id] = current;
                                current = null;
                            }

                            break;
                        case "vertices" when current is not null:
                            vertices = current.Vertices;
                            break;
                        case "vertex" when vertices is not null:
                            vertices.Add(new Vector3(Float(reader, "x"), Float(reader, "y"), Float(reader, "z")));
                            break;
                        case "triangle" when current is not null:
                            current.Faces.Add(new Face(Int(reader, "v1"), Int(reader, "v2"), Int(reader, "v3"), OptionalInt(reader, "pid"), OptionalInt(reader, "p1")));
                            break;
                        case "component" when current is not null:
                            current.Components.Add(new Component(Int(reader, "objectid"), Transform(reader.GetAttribute("transform")), Path(reader)));
                            break;
                        case "item":
                            part.Build.Add((Int(reader, "objectid"), Transform(reader.GetAttribute("transform"))));
                            break;
                    }
                }

                return part;
            }
        }

        private static string? Path(XmlReader reader)
        {
            // The production extension's p:path; any prefix bound to its namespace.
            for (bool more = reader.MoveToFirstAttribute(); more; more = reader.MoveToNextAttribute())
            {
                if (reader.LocalName == "path" && reader.NamespaceURI.Contains("production", StringComparison.Ordinal))
                {
                    string value = reader.Value;
                    reader.MoveToElement();
                    return Normalize(value);
                }
            }

            reader.MoveToElement();
            return null;
        }
    }

    private sealed record ModelObject(int Id, int? Pid, int PIndex)
    {
        public List<Vector3> Vertices { get; } = [];

        public List<Face> Faces { get; } = [];

        public List<Component> Components { get; } = [];
    }

    private readonly record struct Face(int V1, int V2, int V3, int? Pid, int? P1);

    private sealed record Component(int ObjectId, Matrix4x4 Transform, string? Path);

    /// <summary>Walks build items and components into world-space triangles.</summary>
    private sealed class Builder(Package package, MeshLimits limits)
    {
        private readonly HashSet<(string, int)> _visiting = [];

        public List<float> Triangles { get; } = [];

        public List<uint> Colors { get; } = [];

        public bool HasColors { get; private set; }

        public void Emit(string path, int objectId, Matrix4x4 transform, int depth)
        {
            if (depth > limits.MaxDepth)
                throw new ModelException($"components are nested deeper than {limits.MaxDepth.ToString(CultureInfo.InvariantCulture)} levels");
            if (!_visiting.Add((path, objectId)))
                throw new ModelException("its components refer to each other in a cycle");

            Part part = package.Load(path);
            if (!part.Objects.TryGetValue(objectId, out ModelObject? model))
                throw new ModelException($"object {objectId.ToString(CultureInfo.InvariantCulture)} is missing");

            foreach (Face face in model.Faces)
            {
                if (Triangles.Count / 9 >= limits.MaxTriangles)
                    throw new ModelException($"it has more than {limits.MaxTriangles.ToString("N0", CultureInfo.InvariantCulture)} triangles");
                if ((Triangles.Count & 0x3FFFF) == 0)
                    package.Cancellation.ThrowIfCancellationRequested();

                foreach (int index in (int[])[face.V1, face.V2, face.V3])
                {
                    if ((uint)index >= (uint)model.Vertices.Count)
                        throw new ModelException("a triangle refers to a missing vertex");
                    var corner = Vector3.Transform(model.Vertices[index], transform);
                    Triangles.Add(corner.X);
                    Triangles.Add(corner.Y);
                    Triangles.Add(corner.Z);
                }

                uint? color = ColorOf(part, face.Pid ?? model.Pid, face.Pid is null ? model.PIndex : face.P1 ?? model.PIndex);
                HasColors |= color is not null;
                Colors.Add(color ?? 0);
            }

            foreach (Component component in model.Components)
                Emit(component.Path ?? path, component.ObjectId, component.Transform * transform, depth + 1);

            _visiting.Remove((path, objectId));
        }

        private static uint? ColorOf(Part part, int? pid, int index) =>
            pid is { } group && part.ColorGroups.TryGetValue(group, out uint[]? colors) && (uint)index < (uint)colors.Length ? colors[index] : null;
    }

    private sealed class ModelException(string message) : Exception(message);

    /// <summary>Counts unpacked bytes against the package limit while the XML reader pulls them.</summary>
    private sealed class CountingStream(Stream inner, Package package) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            package.Count(read);
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static float Unit(string? unit) => unit switch
    {
        "micron" => 0.001f,
        "centimeter" => 10f,
        "inch" => 25.4f,
        "foot" => 304.8f,
        "meter" => 1000f,
        _ => 1f,
    };

    /// <summary>3MF transforms are 4×3, row vectors: <c>m00 m01 m02 m10 m11 m12 m20 m21 m22 m30 m31 m32</c>.</summary>
    private static Matrix4x4 Transform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Matrix4x4.Identity;
        float[] m = [.. value.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(part => float.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture))];
        if (m.Length != 12 || m.Any(number => !float.IsFinite(number)))
            throw new ModelException("a transform is not 12 numbers");
        return new Matrix4x4(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);
    }

    /// <summary><c>#RRGGBB</c> or <c>#RRGGBBAA</c> as ARGB.</summary>
    private static uint Color(string? value)
    {
        if (value is not { Length: 7 or 9 } || value[0] != '#' || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return 0xFF808080;
        return value.Length == 7 ? 0xFF000000 | rgb : (rgb >> 8) | ((rgb & 0xFF) << 24);
    }

    private static int Int(XmlReader reader, string name) =>
        OptionalInt(reader, name) ?? throw new ModelException($"a {reader.LocalName} has no {name}");

    private static int? OptionalInt(XmlReader reader, string name) =>
        reader.GetAttribute(name) is { } text ? int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture) : null;

    private static float Float(XmlReader reader, string name)
    {
        float value = float.Parse(reader.GetAttribute(name) ?? throw new ModelException($"a vertex has no {name}"), NumberStyles.Float, CultureInfo.InvariantCulture);
        return float.IsFinite(value) ? value : throw new ModelException("a vertex has invalid coordinates");
    }
}
