using System.IO.Compression;
using System.Text;
using Marshal.Abstractions.Artifacts;

namespace Marshal.Artifacts.Tests;

public sealed class ArtifactKindDetectorTests
{
    private readonly ArtifactKindDetector _detector = new();

    public static TheoryData<string, byte[], ArtifactKind, string> Matching => new()
    {
        { "photo.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13], ArtifactKind.Image, "image/png" },
        { "photo.JPG", [0xFF, 0xD8, 0xFF, 0xE0, 0, 16], ArtifactKind.Image, "image/jpeg" },
        { "anim.gif", Encoding.ASCII.GetBytes("GIF89a\x01\x00"), ArtifactKind.Image, "image/gif" },
        { "pic.webp", Encoding.ASCII.GetBytes("RIFF\x10\x00\x00\x00WEBPVP8 "), ArtifactKind.Image, "image/webp" },
        { "clip.mp4", [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'], ArtifactKind.Video, "video/mp4" },
        { "clip.webm", [0x1A, 0x45, 0xDF, 0xA3, 0x9F], ArtifactKind.Video, "video/webm" },
        { "doc.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n"), ArtifactKind.Pdf, "application/pdf" },
        { "icon.svg", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n<!-- logo -->\n<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), ArtifactKind.Svg, "image/svg+xml" },
        { "notes.md", Encoding.UTF8.GetBytes("# Notes\nÜbersicht"), ArtifactKind.Markdown, "text/markdown; charset=utf-8" },
        { "mock.html", Encoding.UTF8.GetBytes("<!doctype html><p>hi</p>"), ArtifactKind.Html, "text/html; charset=utf-8" },
        { "fix.patch", Encoding.UTF8.GetBytes("--- a/x\n+++ b/x\n"), ArtifactKind.Diff, "text/x-diff; charset=utf-8" },
        { "bracket.scad", Encoding.UTF8.GetBytes("cube([10,10,10]);"), ArtifactKind.Code, "text/plain; charset=utf-8" },
        { "ci.log", Encoding.UTF8.GetBytes("build ok"), ArtifactKind.Code, "text/plain; charset=utf-8" },
        { "bracket.stl", Encoding.ASCII.GetBytes("solid bracket\n facet normal 0 0 1\n endfacet\nendsolid"), ArtifactKind.Model3d, "model/stl" },
        { "archive.zip", [0x50, 0x4B, 3, 4], ArtifactKind.Other, "application/octet-stream" },
    };

    [Theory]
    [MemberData(nameof(Matching))]
    public void Detect_ExtensionAndContentAgree(string name, byte[] content, ArtifactKind kind, string mediaType)
    {
        Assert.Equal(new DetectedKind(kind, mediaType), Detect(name, content));
    }

    [Fact]
    public void Detect_BinaryStl_BySizeRule()
    {
        byte[] stl = new byte[84 + (2 * 50)];
        BitConverter.GetBytes(2u).CopyTo(stl, 80);

        Assert.Equal(ArtifactKind.Model3d, Detect("part.stl", stl).Kind);
        Assert.Equal(ArtifactKind.Other, Detect("part.stl", stl[..^1]).Kind);
    }

    [Fact]
    public void Detect_ThreeMf_ZipWithModel()
    {
        Assert.Equal(new DetectedKind(ArtifactKind.Model3d, "model/3mf"), Detect("plate.3mf", Zip("3D/3dmodel.model")));
        Assert.Equal(ArtifactKind.Other, Detect("plate.3mf", Zip("readme.txt")).Kind);
    }

    [Fact]
    public void Detect_DisguisedHtmlAsPng_Other()
    {
        Assert.Equal(DetectedKind.Other, Detect("photo.png", Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>")));
    }

    [Fact]
    public void Detect_BinaryAsMarkdown_Other()
    {
        Assert.Equal(DetectedKind.Other, Detect("notes.md", [0x23, 0x20, 0x00, 0xFF, 0xFE]));
    }

    [Fact]
    public void Detect_SvgWithoutSvgRoot_Other()
    {
        Assert.Equal(DetectedKind.Other, Detect("icon.svg", Encoding.UTF8.GetBytes("<html><svg/></html>")));
    }

    private DetectedKind Detect(string name, byte[] content)
    {
        using var stream = new MemoryStream(content);
        return _detector.Detect(name, stream);
    }

    private static byte[] Zip(string entry)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using StreamWriter writer = new(zip.CreateEntry(entry).Open());
            writer.Write("<model/>");
        }

        return buffer.ToArray();
    }
}
