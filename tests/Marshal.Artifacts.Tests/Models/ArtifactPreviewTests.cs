using System.Numerics;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Artifacts.Models;
using Marshal.Testing;
using SkiaSharp;

namespace Marshal.Artifacts.Tests.Models;

public sealed class ArtifactPreviewTests
{
    private readonly FakeArtifactStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SkiaArtifactRenditions Previews => new(_store);

    [Fact]
    public async Task Model_ShadedViewWithin480x270()
    {
        ArtifactRecord model = await AddAsync("bracket.stl", MeshFixtures.BinaryStl(MeshFixtures.Box(Vector3.Zero, new Vector3(40, 20, 10))));

        Result<ArtifactPreview> preview = await Previews.GetPreviewAsync(model.Id, Ct);

        using var image = SKBitmap.Decode(preview.Value.Png);
        Assert.Equal((480, 270), (image.Width, image.Height));
        Assert.NotEqual(SoftwareMeshRenderer.Background, image.GetPixel(240, 135));
        Assert.Equal(model.Sha256, preview.Value.Sha256);
    }

    [Fact]
    public async Task Image_ScaledToFit()
    {
        ArtifactRecord photo = await AddAsync("photo.png", Png(1000, 500, SKColors.Red));

        using var image = SKBitmap.Decode((await Previews.GetPreviewAsync(photo.Id, Ct)).Value.Png);

        Assert.Equal((480, 240), (image.Width, image.Height));
        Assert.Equal(SKColors.Red, image.GetPixel(240, 120));
    }

    [Fact]
    public async Task SmallImage_NotEnlarged()
    {
        ArtifactRecord photo = await AddAsync("icon.png", Png(64, 32, SKColors.Blue));

        using var image = SKBitmap.Decode((await Previews.GetPreviewAsync(photo.Id, Ct)).Value.Png);

        Assert.Equal((64, 32), (image.Width, image.Height));
    }

    [Theory]
    [InlineData("Program.cs", "class A {}")]
    [InlineData("part.stl", "solid empty\nendsolid empty\n")]
    [InlineData("broken.png", "not an image")]
    public async Task NoPreview_NotFound(string name, string content)
    {
        ArtifactRecord artifact = await AddAsync(name, System.Text.Encoding.UTF8.GetBytes(content));

        Result<ArtifactPreview> preview = await Previews.GetPreviewAsync(artifact.Id, Ct);

        Assert.Equal(ErrorType.NotFound, preview.Error.Type);
    }

    [Fact]
    public async Task UnknownId_NotFound()
    {
        Assert.Equal("artifacts.not_found", (await Previews.GetPreviewAsync(ArtifactId.New(), Ct)).Error.Code);
    }

    private static byte[] Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task<ArtifactRecord> AddAsync(string name, byte[] content) =>
        (await _store.CreateAsync(new ArtifactDraft(name, ArtifactSource.Upload), new MemoryStream(content), Ct)).Value;
}
