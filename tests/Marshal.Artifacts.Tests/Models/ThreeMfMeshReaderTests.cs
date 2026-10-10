using System.Numerics;
using Marshal.Abstractions;
using Marshal.Artifacts.Models;

namespace Marshal.Artifacts.Tests.Models;

public sealed class ThreeMfMeshReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void CoreMesh_Millimetres()
    {
        Mesh mesh = Read(new() { ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="1"/>""", objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(10))) });

        Assert.Equal((12, Vector3.Zero, new Vector3(10)), (mesh.TriangleCount, mesh.Min, mesh.Max));
    }

    [Fact]
    public void Inches_ConvertedToMillimetres()
    {
        Mesh mesh = Read(new() { ["3D/3dmodel.model"] = MeshFixtures.Model(unit: "inch", build: """<item objectid="1"/>""", objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(1))) });

        Assert.Equal(25.4f, mesh.Max.X, 3);
    }

    [Fact]
    public void ItemTransform_Applied()
    {
        Mesh mesh = Read(new() { ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="1" transform="1 0 0 0 1 0 0 0 1 100 50 0"/>""", objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(10))) });

        Assert.Equal((new Vector3(100, 50, 0), new Vector3(110, 60, 10)), (mesh.Min, mesh.Max));
    }

    [Fact]
    public void Components_TransformsComposed()
    {
        string components = """<object id="2" type="model"><components><component objectid="1" transform="2 0 0 0 2 0 0 0 2 0 0 0"/><component objectid="1" transform="1 0 0 0 1 0 0 0 1 30 0 0"/></components></object>""";
        Mesh mesh = Read(new() { ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="2" transform="1 0 0 0 1 0 0 0 1 0 0 5"/>""", objects: [MeshFixtures.MeshObject(1, MeshFixtures.Cube(10)), components]) });

        Assert.Equal((24, new Vector3(0, 0, 5), new Vector3(40, 20, 25)), (mesh.TriangleCount, mesh.Min, mesh.Max));
    }

    [Fact]
    public void BaseMaterialColors_PerObject()
    {
        string materials = """<basematerials id="5"><base name="Red" displaycolor="#FF0000"/><base name="Blue" displaycolor="#0000FFFF"/></basematerials>""";
        Mesh mesh = Read(new()
        {
            ["3D/3dmodel.model"] = MeshFixtures.Model(resources: materials, build: """<item objectid="1"/><item objectid="2"/>""", objects:
            [
                MeshFixtures.MeshObject(1, MeshFixtures.Cube(10), """pid="5" pindex="0" """),
                MeshFixtures.MeshObject(2, MeshFixtures.Box(new Vector3(20, 0, 0), new Vector3(30, 10, 10)), """pid="5" pindex="1" """),
            ]),
        });

        Assert.Equal((0xFFFF0000u, 0xFF0000FFu), (mesh.Colors![0], mesh.Colors[12]));
    }

    [Fact]
    public void ColorGroup_PerTriangle()
    {
        string colors = """<m:colorgroup id="7"><m:color color="#00FF00"/><m:color color="#FFFFFF"/></m:colorgroup>""";
        Mesh mesh = Read(new()
        {
            ["3D/3dmodel.model"] = MeshFixtures.Model(resources: colors, build: """<item objectid="1"/>""", objects:
                MeshFixtures.MeshObject(1, MeshFixtures.Cube(10), triangleAttributes: i => i == 0 ? """pid="7" p1="1" """ : """pid="7" p1="0" """)),
        });

        Assert.Equal((0xFFFFFFFFu, 0xFF00FF00u), (mesh.Colors![0], mesh.Colors[1]));
    }

    [Fact]
    public void ProductionPath_MeshInAnotherPart()
    {
        // The shape slicers (Bambu Studio, PrusaSlicer 2.6+) write: the root holds components pointing into 3D/Objects.
        string root = MeshFixtures.Model(build: """<item objectid="2"/>""", objects: """<object id="2" type="model"><components><component p:path="/3D/Objects/object_1.model" objectid="1"/></components></object>""");
        Mesh mesh = Read(new()
        {
            ["3D/3dmodel.model"] = root,
            ["3D/Objects/object_1.model"] = MeshFixtures.Model(objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(8))),
        });

        Assert.Equal((12, new Vector3(8)), (mesh.TriangleCount, mesh.Max));
    }

    [Fact]
    public void RootFromRelationships()
    {
        byte[] package = MeshFixtures.ThreeMf(new Dictionary<string, string> { ["3D/main.model"] = MeshFixtures.Model(build: """<item objectid="1"/>""", objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(3))) }, rootPath: "3D/main.model");

        Result<Mesh> mesh = new ThreeMfMeshReader().Read(new MemoryStream(package), Ct);

        Assert.Equal(new Vector3(3), mesh.Value.Max);
    }

    [Theory]
    [InlineData("dtd")]
    [InlineData("cycle")]
    [InlineData("deep")]
    [InlineData("no build")]
    [InlineData("not zip")]
    public void Hostile_NoRendition(string kind)
    {
        string cube = MeshFixtures.MeshObject(1, MeshFixtures.Cube(1));
        byte[] package = kind switch
        {
            "dtd" => MeshFixtures.ThreeMf(new Dictionary<string, string> { ["3D/3dmodel.model"] = """<?xml version="1.0"?><!DOCTYPE model [<!ENTITY x "x">]><model unit="millimeter"><resources/><build/></model>""" }),
            "cycle" => MeshFixtures.ThreeMf(new Dictionary<string, string>
            {
                ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="2"/>""", objects:
                [
                    """<object id="2"><components><component objectid="3"/></components></object>""",
                    """<object id="3"><components><component objectid="2"/></components></object>""",
                ]),
            }),
            "deep" => MeshFixtures.ThreeMf(new Dictionary<string, string>
            {
                ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="20"/>""", objects:
                    [cube, .. Enumerable.Range(2, 19).Select(id => $"""<object id="{id}"><components><component objectid="{id - 1}"/></components></object>""")]),
            }),
            "no build" => MeshFixtures.ThreeMf(new Dictionary<string, string> { ["3D/3dmodel.model"] = MeshFixtures.Model(objects: cube) }),
            _ => "PK not really"u8.ToArray(),
        };

        Assert.Equal("artifacts.no_rendition", new ThreeMfMeshReader().Read(new MemoryStream(package), Ct).Error.Code);
    }

    [Fact]
    public void TooManyEntries_NoRendition()
    {
        Dictionary<string, string> parts = new() { ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="1"/>""", objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(1))) };
        foreach (int i in Enumerable.Range(0, 1001))
            parts[$"Metadata/{i}.txt"] = "x";

        Assert.Equal("artifacts.no_rendition", new ThreeMfMeshReader().Read(new MemoryStream(MeshFixtures.ThreeMf(parts)), Ct).Error.Code);
    }

    [Fact]
    public void UncompressedLimit_NoRendition()
    {
        Dictionary<string, string> parts = new() { ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="1"/>""", objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(1))) };
        var reader = new ThreeMfMeshReader(MeshLimits.Default with { MaxUncompressedBytes = 1000 });

        Assert.Equal("artifacts.no_rendition", reader.Read(new MemoryStream(MeshFixtures.ThreeMf(parts)), Ct).Error.Code);
    }

    [Fact]
    public void TriangleLimit_NoRendition()
    {
        Dictionary<string, string> parts = new() { ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="1"/><item objectid="1"/>""", objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(1))) };
        var reader = new ThreeMfMeshReader(MeshLimits.Default with { MaxTriangles = 20 });

        Assert.Equal("artifacts.no_rendition", reader.Read(new MemoryStream(MeshFixtures.ThreeMf(parts)), Ct).Error.Code);
    }

    private static Mesh Read(Dictionary<string, string> parts)
    {
        Result<Mesh> result = new ThreeMfMeshReader().Read(new MemoryStream(MeshFixtures.ThreeMf(parts)), Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }
}
