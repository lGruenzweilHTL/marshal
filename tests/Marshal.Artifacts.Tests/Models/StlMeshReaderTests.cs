using System.Numerics;
using System.Text;
using Marshal.Abstractions;
using Marshal.Artifacts.Models;

namespace Marshal.Artifacts.Tests.Models;

public sealed class StlMeshReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Binary_TrianglesAndBounds()
    {
        Mesh mesh = Read(MeshFixtures.BinaryStl(MeshFixtures.Box(new Vector3(-20, -10, 0), new Vector3(20, 10, 10))));

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal((new Vector3(-20, -10, 0), new Vector3(20, 10, 10)), (mesh.Min, mesh.Max));
        Assert.Null(mesh.Colors);
    }

    [Fact]
    public void Ascii_TrianglesAndBounds()
    {
        Mesh mesh = Read(MeshFixtures.AsciiStl(MeshFixtures.Cube(5)));

        Assert.Equal((12, new Vector3(5)), (mesh.TriangleCount, mesh.Max));
    }

    [Fact]
    public void BinaryStartingWithSolid_ReadAsBinary()
    {
        Mesh mesh = Read(MeshFixtures.BinaryStl(MeshFixtures.Cube(), header: "solid exported by a CAD tool"));

        Assert.Equal(12, mesh.TriangleCount);
    }

    [Theory]
    [InlineData("wrong size")]
    [InlineData("nan")]
    [InlineData("empty")]
    [InlineData("ascii empty")]
    [InlineData("garbage")]
    public void Unreadable_NoRendition(string kind)
    {
        byte[] content = kind switch
        {
            "wrong size" => [.. MeshFixtures.BinaryStl(MeshFixtures.Cube()), 1, 2, 3],
            "nan" => MeshFixtures.BinaryStl([[new Vector3(float.NaN, 0, 0), Vector3.UnitX, Vector3.UnitY]]),
            "empty" => MeshFixtures.BinaryStl([]),
            "ascii empty" => Encoding.ASCII.GetBytes("solid empty\nendsolid empty\n"),
            _ => Encoding.ASCII.GetBytes("this is not a model"),
        };

        Result<Mesh> result = new StlMeshReader().Read(new MemoryStream(content), Ct);

        Assert.Equal("artifacts.no_rendition", result.Error.Code);
    }

    [Fact]
    public void TooManyTriangles_NoRendition()
    {
        byte[] header = new byte[84];
        BitConverter.GetBytes(5_000_001u).CopyTo(header, 80);

        Result<Mesh> result = new StlMeshReader().Read(new SizedStream(header, 84 + 50L * 5_000_001), Ct);

        Assert.Equal("artifacts.no_rendition", result.Error.Code);
        Assert.Contains("5000000", result.Error.Message.Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static Mesh Read(byte[] content)
    {
        Result<Mesh> result = new StlMeshReader().Read(new MemoryStream(content), Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    /// <summary>A stream that claims a large length but holds only its header, so the count check runs without 250 MB.</summary>
    private sealed class SizedStream(byte[] head, long length) : MemoryStream(head)
    {
        public override long Length => length;
    }
}
