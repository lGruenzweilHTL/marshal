using System.Numerics;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Displays;
using Marshal.Artifacts.Models;
using Marshal.Testing;
using SkiaSharp;

namespace Marshal.Artifacts.Tests.Models;

public sealed class ModelRenditionTests : IDisposable
{
    private readonly FakeArtifactStore _store = new();
    private readonly string _cacheDirectory = Directory.CreateTempSubdirectory("marshal-renditions-").FullName;
    private readonly RecordingCache _cache;

    public ModelRenditionTests() => _cache = new RecordingCache(new FileRenditionCache(_cacheDirectory));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SkiaArtifactRenditions Renditions => new(_store, _cache);

    public void Dispose() => Directory.Delete(_cacheDirectory, recursive: true);

    [Fact]
    public async Task Describe_Stl_TwoPngPages()
    {
        ArtifactRecord model = await AddAsync("bracket.stl", Bracket());

        Result<ArtifactRendition> rendition = await Renditions.DescribeAsync(model.Id, Ct);

        Assert.True(rendition.IsSuccess, rendition.IsFailure ? rendition.Error.Message : null);
        Assert.Equal((2, "image/png", "png", DisplayMediaType.Image), (rendition.Value.Pages, rendition.Value.MediaType, rendition.Value.Extension, rendition.Value.DisplayType));
    }

    [Fact]
    public void Header_NamesPageAndSizeInMillimetres()
    {
        var mesh = new Mesh([.. MeshFixtures.Box(Vector3.Zero, new Vector3(40, 20, 10)).SelectMany(t => t).SelectMany(v => (float[])[v.X, v.Y, v.Z])], null);

        Assert.Equal("bracket.stl — page 1/2 — 40.0 × 20.0 × 10.0 mm", SkiaArtifactRenditions.ModelHeader("bracket.stl", mesh, 1));
    }

    [Fact]
    public async Task Page1_ShadedViewFillsThePage()
    {
        ArtifactRecord model = await AddAsync("bracket.stl", Bracket());

        using SKBitmap page = await OpenAsync(model, 1);

        Assert.Equal((1920, 1080), (page.Width, page.Height));
        Assert.NotEqual(SoftwareMeshRenderer.Background, page.GetPixel(960, 568));
        Assert.Equal(SoftwareMeshRenderer.Background, page.GetPixel(10, 1070));
    }

    [Fact]
    public async Task Page2_FourViews()
    {
        ArtifactRecord model = await AddAsync("bracket.3mf", MeshFixtures.ThreeMf(new Dictionary<string, string>
        {
            ["3D/3dmodel.model"] = MeshFixtures.Model(build: """<item objectid="1"/>""", objects: MeshFixtures.MeshObject(1, MeshFixtures.Cube(10))),
        }));

        using SKBitmap page = await OpenAsync(model, 2);

        foreach ((int x, int y) in SkiaArtifactRenditions.TileCenters())
            Assert.NotEqual(SoftwareMeshRenderer.Background, page.GetPixel(x, y));
    }

    [Fact]
    public async Task Unreadable_NoRendition()
    {
        ArtifactRecord model = await AddAsync("part.stl", "solid empty\nendsolid empty\n"u8.ToArray());

        Result<ArtifactRendition> rendition = await Renditions.DescribeAsync(model.Id, Ct);

        Assert.Equal("artifacts.no_rendition", rendition.Error.Code);
        Assert.Contains("3D model", rendition.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondRequest_FromCacheByHash()
    {
        ArtifactRecord model = await AddAsync("bracket.stl", Bracket());

        await Renditions.DescribeAsync(model.Id, Ct);
        await (await Renditions.OpenAsync(model.Id, 1, Ct)).Value.DisposeAsync();

        Assert.Equal([$"{model.Sha256}-p1"], _cache.Stored);
        Assert.Contains($"{model.Sha256}-p1", _cache.Hits);
    }

    [Fact]
    public async Task DeletedArtifact_NotFoundEvenWhenCached()
    {
        ArtifactRecord model = await AddAsync("bracket.stl", Bracket());
        await Renditions.DescribeAsync(model.Id, Ct);
        await _store.DeleteAsync(model.Id, Ct);

        Assert.Equal("artifacts.not_found", (await Renditions.OpenAsync(model.Id, 1, Ct)).Error.Code);
    }

    private static byte[] Bracket() => MeshFixtures.BinaryStl(MeshFixtures.Box(Vector3.Zero, new Vector3(40, 20, 10)));

    private async Task<SKBitmap> OpenAsync(ArtifactRecord artifact, int page)
    {
        Result<Stream> opened = await Renditions.OpenAsync(artifact.Id, page, Ct);
        Assert.True(opened.IsSuccess, opened.IsFailure ? opened.Error.Message : null);
        await using Stream png = opened.Value;
        return SKBitmap.Decode(png);
    }

    private async Task<ArtifactRecord> AddAsync(string name, byte[] content) =>
        (await _store.CreateAsync(new ArtifactDraft(name, ArtifactSource.Upload), new MemoryStream(content), Ct)).Value;

    private sealed class RecordingCache(IRenditionCache inner) : IRenditionCache
    {
        public List<string> Stored { get; } = [];

        public List<string> Hits { get; } = [];

        public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
        {
            byte[]? found = await inner.GetAsync(key, cancellationToken);
            if (found is not null)
                Hits.Add(key);
            return found;
        }

        public Task SetAsync(string key, byte[] content, CancellationToken cancellationToken)
        {
            Stored.Add(key);
            return inner.SetAsync(key, content, cancellationToken);
        }
    }
}
