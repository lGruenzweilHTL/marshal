using Marshal.Abstractions.Memory;
using Marshal.Memory.Retrieval;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests.Retrieval;

public sealed class HybridMemoryRetrieverTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private TestDatabase _database = null!;
    private SqliteMemoryStore _store = null!;
    private HybridMemoryRetriever _retriever = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _store = MemoryTestKit.Store(_database, _embeddings, _time);
        _retriever = MemoryTestKit.Retriever(_database, _embeddings, _time);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Retrieve_VectorSeed_SimilarChunkFound()
    {
        ChunkId oat = await WriteAsync("Coffee", "Likes oat milk.", Vec(0, 1), ChunkType.Preference);
        await WriteAsync("Garden", "Tomatoes in the greenhouse.", Vec(5, 1));
        _embeddings.Pin("How do I like my coffee?", Vec(0, 1, 1, 0.1f));

        IReadOnlyList<RetrievedChunk> result = await RetrieveAsync("How do I like my coffee?");

        RetrievedChunk hit = Assert.Single(result, chunk => chunk.Chunk.Id == oat);
        Assert.Equal(RetrievalSource.Vector, hit.Source);
        Assert.DoesNotContain(result, chunk => chunk.Chunk.Title == "Garden");
    }

    [Fact]
    public async Task Retrieve_KeywordOnly_ExactNameIncludedAsSeed()
    {
        ChunkId voron = await WriteAsync("Voron", "The Voron 2.4 printer lives in the basement.", Vec(3, 1), ChunkType.Device);
        _embeddings.Pin("Is the Voron busy?", Vec(9, 1));

        IReadOnlyList<RetrievedChunk> result = await RetrieveAsync("Is the Voron busy?");

        RetrievedChunk hit = Assert.Single(result);
        Assert.Equal((voron, RetrievalSource.Keyword), (hit.Chunk.Id, hit.Source));
    }

    [Fact]
    public async Task Retrieve_EdgeExpansion_RelatedChunkAdded()
    {
        ChunkId printer = await WriteAsync("Printer", "Prints PETG.", Vec(0, 1), ChunkType.Device);
        ChunkId filament = await WriteAsync("Filament storage", "Dry box in the hallway.", Vec(7, 1));
        await _store.LinkAsync(printer, filament, ChunkEdgeKind.RelatesTo, Ct);
        _embeddings.Pin("printer", Vec(0, 1));

        IReadOnlyList<RetrievedChunk> result = await RetrieveAsync("printer");

        Assert.Equal(RetrievalSource.Edge, Assert.Single(result, chunk => chunk.Chunk.Id == filament).Source);
    }

    [Fact]
    public async Task Retrieve_SupersededSeed_SuccessorReturnedInstead()
    {
        ChunkId old = await WriteAsync("Nozzle", "0.4 mm", Vec(0, 1), ChunkType.Device);
        _embeddings.Pin("Voron nozzle\n0.6 mm", Vec(8, 1));
        ChunkId successor = (await _store.SupersedeAsync(old, new ChunkDraft(ChunkScope.General, null, ChunkType.Device, "Voron nozzle", "0.6 mm"), ChunkAuthor.User, Ct)).Value.Id;
        _embeddings.Pin("which nozzle", Vec(0, 1));

        IReadOnlyList<RetrievedChunk> result = await RetrieveAsync("which nozzle");

        RetrievedChunk hit = Assert.Single(result);
        Assert.Equal((successor, RetrievalSource.Successor), (hit.Chunk.Id, hit.Source));
    }

    [Fact]
    public async Task Retrieve_SoftDeletedAndPinned_Excluded_PinnedListedSeparately()
    {
        ChunkId deleted = await WriteAsync("Coffee", "Likes oat milk.", Vec(0, 1));
        ChunkId pinned = await WriteAsync("Allergy", "Allergic to peanuts.", Vec(0, 1, 1, 0.2f));
        ChunkId other = await WriteAsync("Unrelated", "Something else.", Vec(12, 1));
        await _store.DeleteAsync(deleted, Ct);
        await _store.SetPinnedAsync(pinned, pinned: true, Ct);
        await _store.SetPinnedAsync(other, pinned: true, Ct);
        _embeddings.Pin("coffee", Vec(0, 1));

        IReadOnlyList<RetrievedChunk> result = await RetrieveAsync("coffee");

        Assert.Empty(result);
        Assert.Equal(new HashSet<ChunkId> { pinned, other }, (await _retriever.GetPinnedAsync(Ct)).Select(chunk => chunk.Id).ToHashSet());
    }

    [Fact]
    public async Task Retrieve_OtherOwnersChunks_NeverReturned()
    {
        await WriteAsync("Coffee", "Likes oat milk.", Vec(0, 1));
        _database.Accessor.Current = TestExecutionContext.Text(TestExecutionContext.OwnerB);
        _embeddings.Pin("coffee", Vec(0, 1));

        Assert.Empty(await RetrieveAsync("coffee"));
        Assert.Empty(await _retriever.GetPinnedAsync(Ct));
    }

    [Fact]
    public async Task Retrieve_MarkRetrievedOff_LastRetrievedUnchanged()
    {
        ChunkId oat = await WriteAsync("Coffee", "Likes oat milk.", Vec(0, 1));
        _embeddings.Pin("coffee", Vec(0, 1));

        IReadOnlyList<RetrievedChunk> found = await _retriever.RetrieveAsync(new MemoryQuery("coffee", null, new HashSet<ProjectId>(), 600, MarkRetrieved: false), Ct);

        Assert.Equal(oat, Assert.Single(found).Chunk.Id);
        Assert.Null((await _store.GetAsync(oat, Ct))!.LastRetrievedAt);
    }

    [Fact]
    public async Task Retrieve_MarksLastRetrieved()
    {
        ChunkId oat = await WriteAsync("Coffee", "Likes oat milk.", Vec(0, 1));
        _time.Advance(TimeSpan.FromHours(2));
        _embeddings.Pin("coffee", Vec(0, 1));

        await RetrieveAsync("coffee");

        Assert.Equal(_time.GetUtcNow(), (await _store.GetAsync(oat, Ct))!.LastRetrievedAt);
    }

    private Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string text) =>
        _retriever.RetrieveAsync(new MemoryQuery(text, null, new HashSet<ProjectId>(), 1000), Ct);

    private async Task<ChunkId> WriteAsync(string title, string content, float[] vector, ChunkType type = ChunkType.Fact)
    {
        _embeddings.Pin($"{title}\n{content}", vector);
        return (await _store.WriteAsync(new ChunkDraft(ChunkScope.General, null, type, title, content), ChunkAuthor.User, Ct)).Value.Id;
    }

    /// <summary>A 16-dimensional vector with the given axis weights (axis, weight, axis, weight, ...).</summary>
    internal static float[] Vec(params float[] axesAndWeights)
    {
        float[] vector = new float[16];
        for (int index = 0; index + 1 < axesAndWeights.Length; index += 2)
            vector[(int)axesAndWeights[index]] = axesAndWeights[index + 1];

        return vector;
    }
}
