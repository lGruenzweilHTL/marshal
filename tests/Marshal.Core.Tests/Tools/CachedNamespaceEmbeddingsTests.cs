using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Marshal.Testing;

namespace Marshal.Core.Tests.Tools;

public sealed class CachedNamespaceEmbeddingsTests
{
    private static readonly ToolNamespace _printer = new("printer", "3D printers");
    private static readonly ToolNamespace _music = new("music", "Music playback");

    private readonly FakeEmbeddingGenerator _generator = new(modelId: "bge-m3");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_OneEmbeddingPerDescription()
    {
        var cache = new CachedNamespaceEmbeddings(_generator);

        IReadOnlyDictionary<string, ReadOnlyMemory<float>> embeddings = await cache.GetAsync([_printer, _music], Ct);

        Assert.Equal(["music", "printer"], embeddings.Keys.Order());
        Assert.Equal(2, _generator.CallCount);
    }

    [Fact]
    public async Task Get_UnchangedDescriptions_CacheHit()
    {
        var cache = new CachedNamespaceEmbeddings(_generator);
        await cache.GetAsync([_printer, _music], Ct);

        await cache.GetAsync([_printer, _music], Ct);

        Assert.Equal(2, _generator.CallCount);
    }

    [Fact]
    public async Task Get_AfterSourceReloadWithChangedDescription_EmbedsOnlyTheChangedOne()
    {
        var cache = new CachedNamespaceEmbeddings(_generator);
        IReadOnlyDictionary<string, ReadOnlyMemory<float>> before = await cache.GetAsync([_printer, _music], Ct);

        IReadOnlyDictionary<string, ReadOnlyMemory<float>> after = await cache.GetAsync([_printer, _music with { Description = "Speakers, songs and radio" }], Ct);

        Assert.Equal(3, _generator.CallCount);
        Assert.False(before["music"].Span.SequenceEqual(after["music"].Span));
        Assert.True(before["printer"].Span.SequenceEqual(after["printer"].Span));
    }
}
