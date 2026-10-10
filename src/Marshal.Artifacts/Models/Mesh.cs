using System.Numerics;
using Marshal.Abstractions;

namespace Marshal.Artifacts.Models;

/// <summary>
/// Triangles in millimetres, nine floats each (three corners), Z up, with an optional ARGB color per triangle
/// (null: one default color). Read from STL or 3MF by an <see cref="IMeshReader"/>.
/// </summary>
internal sealed class Mesh
{
    public Mesh(float[] triangles, uint[]? colors)
    {
        Triangles = triangles;
        Colors = colors;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var corner = new Vector3(triangles[i], triangles[i + 1], triangles[i + 2]);
            min = Vector3.Min(min, corner);
            max = Vector3.Max(max, corner);
        }

        Min = min;
        Max = max;
    }

    public float[] Triangles { get; }

    public uint[]? Colors { get; }

    public int TriangleCount => Triangles.Length / 9;

    public Vector3 Min { get; }

    public Vector3 Max { get; }

    public Vector3 Size => Max - Min;
}

/// <summary>Reads one model format; every failure is <c>artifacts.no_rendition</c>.</summary>
internal interface IMeshReader
{
    Result<Mesh> Read(Stream content, CancellationToken cancellationToken);
}

/// <summary>Bounds for hostile files: models can come from mail and executors.</summary>
internal sealed record MeshLimits(int MaxTriangles, int MaxVertices, int MaxEntries, long MaxUncompressedBytes, int MaxDepth)
{
    public static MeshLimits Default { get; } = new(5_000_000, 15_000_000, 1_000, 1L << 30, 16);
}

internal static class MeshErrors
{
    public static Error Unreadable(string reason) =>
        new("artifacts.no_rendition", $"The 3D model cannot be shown: {reason}.", ErrorType.Validation);

    public static Error TooManyTriangles(int limit) =>
        Unreadable($"it has more than {limit.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} triangles");
}
