using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Displays;
using Marshal.Testing;
using SkiaSharp;

namespace Marshal.Artifacts.Tests;

public sealed class SkiaArtifactRenditionsTests
{
    private readonly FakeArtifactStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SkiaArtifactRenditions Renditions => new(_store);

    [Theory]
    [InlineData("photo.jpg", "image/jpeg", "jpg", DisplayMediaType.Image)]
    [InlineData("photo.png", "image/png", "png", DisplayMediaType.Image)]
    [InlineData("anim.gif", "image/png", "png", DisplayMediaType.Image)]
    [InlineData("photo.webp", "image/png", "png", DisplayMediaType.Image)]
    [InlineData("clip.mp4", "video/mp4", "mp4", DisplayMediaType.Video)]
    [InlineData("Program.cs", "image/png", "png", DisplayMediaType.Image)]
    [InlineData("fix.diff", "image/png", "png", DisplayMediaType.Image)]
    [InlineData("notes.md", "image/png", "png", DisplayMediaType.Image)]
    public async Task Describe_KindsWithRendition(string name, string mediaType, string extension, DisplayMediaType type)
    {
        ArtifactRecord artifact = await AddAsync(name, "x"u8.ToArray());

        Result<ArtifactRendition> rendition = await Renditions.DescribeAsync(artifact.Id, Ct);

        Assert.True(rendition.IsSuccess);
        Assert.Equal((1, mediaType, extension, type), (rendition.Value.Pages, rendition.Value.MediaType, rendition.Value.Extension, rendition.Value.DisplayType));
    }

    [Theory]
    [InlineData("bracket.stl")]
    [InlineData("clip.webm")]
    [InlineData("logo.svg")]
    [InlineData("manual.pdf")]
    [InlineData("blob.bin")]
    public async Task Describe_KindsWithoutRendition_Fail(string name)
    {
        ArtifactRecord artifact = await AddAsync(name, "x"u8.ToArray());

        Result<ArtifactRendition> rendition = await Renditions.DescribeAsync(artifact.Id, Ct);

        Assert.Equal("artifacts.no_rendition", rendition.Error.Code);
        Assert.Contains("MP4 video", rendition.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_UnknownId_NotFound()
    {
        Result<ArtifactRendition> rendition = await Renditions.DescribeAsync(ArtifactId.New(), Ct);

        Assert.Equal("artifacts.not_found", rendition.Error.Code);
    }

    [Fact]
    public async Task Describe_130Lines_FourPages()
    {
        ArtifactRecord artifact = await AddAsync("Program.cs", Lines(130));

        Assert.Equal(4, (await Renditions.DescribeAsync(artifact.Id, Ct)).Value.Pages);
    }

    [Fact]
    public void Layout_SecondPageStartsAtLine41()
    {
        IReadOnlyList<SkiaArtifactRenditions.Row> rows = SkiaArtifactRenditions.Layout(Encoding.UTF8.GetString(Lines(130)), diff: false, truncated: false, columns: 120);

        Assert.Equal(130, rows.Count);
        Assert.Equal(41, rows[SkiaArtifactRenditions.RowsPerPage].Line);
        Assert.Equal(80, rows[(2 * SkiaArtifactRenditions.RowsPerPage) - 1].Line);
    }

    [Fact]
    public void Layout_WrapsLongLinesAndExpandsTabs()
    {
        IReadOnlyList<SkiaArtifactRenditions.Row> rows = SkiaArtifactRenditions.Layout("\t" + new string('a', 20) + "\nshort\n", diff: false, truncated: false, columns: 10);

        Assert.Equal([1, null, null, 2], rows.Select(row => row.Line));
        Assert.Equal("    aaaaaa", rows[0].Text);
        Assert.Equal("short", rows[3].Text);
    }

    [Fact]
    public void Layout_DiffColors()
    {
        IReadOnlyList<SkiaArtifactRenditions.Row> rows = SkiaArtifactRenditions.Layout("--- a/x\n+++ b/x\n@@ -1 +1 @@\n-old\n+new\n same", diff: true, truncated: false, columns: 80);

        Assert.Equal(
            [SkiaArtifactRenditions.RowStyle.Plain, SkiaArtifactRenditions.RowStyle.Plain, SkiaArtifactRenditions.RowStyle.Hunk,
             SkiaArtifactRenditions.RowStyle.Removed, SkiaArtifactRenditions.RowStyle.Added, SkiaArtifactRenditions.RowStyle.Plain],
            rows.Select(row => row.Style));
    }

    [Fact]
    public void Layout_Truncated_AddsNote()
    {
        IReadOnlyList<SkiaArtifactRenditions.Row> rows = SkiaArtifactRenditions.Layout("a\n", diff: false, truncated: true, columns: 80);

        Assert.Equal(2, rows.Count);
        Assert.Contains("cut after 2 MB", rows[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_TextPage_Is1080pPng()
    {
        ArtifactRecord artifact = await AddAsync("Program.cs", Lines(130));

        Result<Stream> page = await Renditions.OpenAsync(artifact.Id, 2, Ct);

        using SKBitmap bitmap = Decode(page.Value);
        Assert.Equal((1920, 1080), (bitmap.Width, bitmap.Height));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Open_PageOutOfRange_Fails(int page)
    {
        ArtifactRecord artifact = await AddAsync("Program.cs", Lines(130));

        Result<Stream> opened = await Renditions.OpenAsync(artifact.Id, page, Ct);

        Assert.Equal("artifacts.invalid_arguments", opened.Error.Code);
        Assert.Contains("4 pages", opened.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_Jpeg_AsStored()
    {
        byte[] jpeg = Encode(SKEncodedImageFormat.Jpeg);
        ArtifactRecord artifact = await AddAsync("photo.jpg", jpeg);

        Result<Stream> opened = await Renditions.OpenAsync(artifact.Id, 1, Ct);

        using var copy = new MemoryStream();
        await opened.Value.CopyToAsync(copy, Ct);
        Assert.Equal(jpeg, copy.ToArray());
    }

    [Fact]
    public async Task Open_WebP_ConvertedToPng()
    {
        ArtifactRecord artifact = await AddAsync("photo.webp", Encode(SKEncodedImageFormat.Webp));

        Result<Stream> opened = await Renditions.OpenAsync(artifact.Id, 1, Ct);

        using var copy = new MemoryStream();
        await opened.Value.CopyToAsync(copy, Ct);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], copy.ToArray()[..4]);
    }

    [Fact]
    public async Task Open_UndecodableWebP_Fails()
    {
        ArtifactRecord artifact = await AddAsync("photo.webp", "not an image"u8.ToArray());

        Result<Stream> opened = await Renditions.OpenAsync(artifact.Id, 1, Ct);

        Assert.Equal("artifacts.no_rendition", opened.Error.Code);
    }

    private async Task<ArtifactRecord> AddAsync(string name, byte[] content) =>
        (await _store.CreateAsync(new ArtifactDraft(name, ArtifactSource.Upload), new MemoryStream(content), Ct)).Value;

    private static byte[] Lines(int count) => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, count).Select(line => $"line {line}\n")));

    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Orange);
        using SKData data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    private static SKBitmap Decode(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return SKBitmap.Decode(copy.ToArray());
    }
}
