using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace Marshal.Artifacts.Tests.Models;

/// <summary>STL and 3MF files built in code: small, exact and readable in the test.</summary>
internal static class MeshFixtures
{
    /// <summary>The 12 triangles of an axis-aligned box from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static Vector3[][] Box(Vector3 min, Vector3 max)
    {
        Vector3 C(int x, int y, int z) => new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
        int[][] quads = [[0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0], [0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 1], [0, 0, 0, 0, 0, 1, 1, 0, 1, 1, 0, 0],
                         [0, 1, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1], [0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1], [1, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0]];
        List<Vector3[]> triangles = [];
        foreach (int[] q in quads)
        {
            Vector3 a = C(q[0], q[1], q[2]), b = C(q[3], q[4], q[5]), c = C(q[6], q[7], q[8]), d = C(q[9], q[10], q[11]);
            triangles.Add([a, b, c]);
            triangles.Add([a, c, d]);
        }

        return [.. triangles];
    }

    public static Vector3[][] Cube(float size = 10) => Box(Vector3.Zero, new Vector3(size));

    public static byte[] BinaryStl(Vector3[][] triangles, string header = "binary")
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            byte[] head = new byte[80];
            Encoding.ASCII.GetBytes(header).CopyTo(head, 0);
            writer.Write(head);
            writer.Write((uint)triangles.Length);
            foreach (Vector3[] triangle in triangles)
            {
                writer.Write(0f); writer.Write(0f); writer.Write(0f);
                foreach (Vector3 vertex in triangle)
                {
                    writer.Write(vertex.X); writer.Write(vertex.Y); writer.Write(vertex.Z);
                }

                writer.Write((ushort)0);
            }
        }

        return stream.ToArray();
    }

    public static byte[] AsciiStl(Vector3[][] triangles)
    {
        var text = new StringBuilder("solid test\n");
        foreach (Vector3[] triangle in triangles)
        {
            text.Append("  facet normal 0 0 0\n    outer loop\n");
            foreach (Vector3 vertex in triangle)
                text.Append(CultureInfo.InvariantCulture, $"      vertex {vertex.X} {vertex.Y} {vertex.Z}\n");
            text.Append("    endloop\n  endfacet\n");
        }

        text.Append("endsolid test\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>A 3MF package: <paramref name="parts"/> maps part paths (without leading slash) to XML.</summary>
    public static byte[] ThreeMf(IReadOnlyDictionary<string, string> parts, string rootPath = "3D/3dmodel.model")
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/></Types>""");
            Add(zip, "_rels/.rels", $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Target="/{rootPath}" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/></Relationships>""");
            foreach ((string path, string xml) in parts)
                Add(zip, path, xml);
        }

        return stream.ToArray();
    }

    /// <summary>A model part with one mesh object per entry of <paramref name="objects"/> and a build item for each.</summary>
    public static string Model(string unit = "millimeter", string resources = "", string build = "", params string[] objects) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><model unit="{unit}" xml:lang="en-US" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02" xmlns:m="http://schemas.microsoft.com/3dmanufacturing/material/2015/02" xmlns:p="http://schemas.microsoft.com/3dmanufacturing/production/2015/06"><resources>{resources}{string.Concat(objects)}</resources><build>{build}</build></model>""";

    /// <summary>A mesh object; <paramref name="attributes"/> go on the object element.</summary>
    public static string MeshObject(int id, Vector3[][] triangles, string attributes = "", Func<int, string>? triangleAttributes = null)
    {
        var xml = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"""<object id="{id}" type="model" {attributes}><mesh><vertices>"""));
        foreach (Vector3 vertex in triangles.SelectMany(triangle => triangle))
            xml.Append(CultureInfo.InvariantCulture, $"""<vertex x="{vertex.X}" y="{vertex.Y}" z="{vertex.Z}"/>""");
        xml.Append("</vertices><triangles>");
        for (int i = 0; i < triangles.Length; i++)
            xml.Append(CultureInfo.InvariantCulture, $"""<triangle v1="{3 * i}" v2="{3 * i + 1}" v3="{3 * i + 2}" {triangleAttributes?.Invoke(i)}/>""");
        xml.Append("</triangles></mesh></object>");
        return xml.ToString();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        using Stream entry = zip.CreateEntry(path).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}
