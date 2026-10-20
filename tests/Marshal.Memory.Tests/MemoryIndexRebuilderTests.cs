using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Memory.Tests.Projects;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests;

public sealed class MemoryIndexRebuilderTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _old = new(16, "model-a");
    private readonly FakeEmbeddingGenerator _new = new(8, "model-b");
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task ModelChanged_ReEmbedsEveryOwnersChunksAndFeedbackAndDropsCentroids()
    {
        ChunkId ownChunk = await WriteAsync(TestExecutionContext.OwnerA, "Voron nozzle", "0.6 mm.");
        ChunkId otherChunk = await WriteAsync(TestExecutionContext.OwnerB, "Coffee", "Oat milk.");
        ChunkId deleted = await WriteAsync(TestExecutionContext.OwnerA, "Old printer", "Ender 3.");
        await MemoryTestKit.Store(_database, _old, _time).DeleteAsync(deleted, Ct);
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron");
        await ProjectTestKit.Learning(_database, new MemoryOptions(), _time).RecordAsync(voron, ProjectFeedbackKind.Rejected, "the printer thing", await MemoryTestKit.EmbedAsync(_old, "the printer thing"), null, Ct);
        await SetCentroidAsync(voron);

        bool rebuilt = await Rebuilder(_new).RebuildIfStaleAsync(Ct);

        Assert.True(rebuilt);
        Assert.Equal(("model-b", 8), await InfoAsync());
        Assert.Equal(await MemoryTestKit.EmbedAsync(_new, "Voron nozzle\n0.6 mm."), await MemoryTestKit.StoredVectorAsync(_database, ownChunk));
        Assert.Equal(8, (await MemoryTestKit.StoredVectorAsync(_database, otherChunk))?.Length);
        Assert.Null(await MemoryTestKit.StoredVectorAsync(_database, deleted));
        await using MarshalDbContext db = _database.CreateContext();
        Assert.Equal(await MemoryTestKit.EmbedAsync(_new, "the printer thing"), Vectors.FromBlob((await db.ProjectFeedback.SingleAsync(Ct)).Embedding!));
        Assert.Null((await db.Projects.SingleAsync(Ct)).Centroid);
    }

    [Fact]
    public async Task SameModel_NothingEmbedded()
    {
        await WriteAsync(TestExecutionContext.OwnerA, "Voron nozzle", "0.6 mm.");
        int before = _old.CallCount;

        Assert.False(await Rebuilder(_old).RebuildIfStaleAsync(Ct));
        Assert.Equal(before, _old.CallCount);
    }

    [Fact]
    public async Task Interrupted_RebuildsAgainOnTheNextStart()
    {
        await WriteAsync(TestExecutionContext.OwnerA, "Voron nozzle", "0.6 mm.");
        await WriteAsync(TestExecutionContext.OwnerB, "Coffee", "Oat milk.");

        await Assert.ThrowsAsync<HttpRequestException>(() => Rebuilder(new FailingAfterFirst(_new)).RebuildIfStaleAsync(Ct));
        bool rebuilt = await Rebuilder(_new).RebuildIfStaleAsync(Ct);

        Assert.True(rebuilt);
        Assert.Equal(("model-b", 8), await InfoAsync());
    }

    [Fact]
    public async Task NoIndexYet_NothingToDo()
    {
        Assert.False(await Rebuilder(_new).RebuildIfStaleAsync(Ct));
        Assert.Null(await InfoAsync());
    }

    [Fact]
    public async Task VersionsWithoutKeywordEntries_IndexedOnce()
    {
        ChunkId nozzle = await WriteAsync(TestExecutionContext.OwnerA, "Voron nozzle", "0.4 mm brass.");
        await WriteAsync(TestExecutionContext.OwnerA, "Voron nozzle", "0.6 mm steel.");
        await using (MarshalDbContext db = _database.CreateContext())
        {
            // A version written before the version keyword index existed.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM chunk_versions_fts", Ct);
        }

        int indexed = await Rebuilder(_old).IndexVersionsAsync(Ct);
        int again = await Rebuilder(_old).IndexVersionsAsync(Ct);

        Assert.Equal((1, 0), (indexed, again));
        Assert.Equal([nozzle], await VersionHitsAsync("brass"));
    }

    private async Task<List<ChunkId>> VersionHitsAsync(string word)
    {
        await using MarshalDbContext db = _database.CreateContext();
        await db.Database.OpenConnectionAsync(Ct);
        await using System.Data.Common.DbCommand command = MemoryIndexes.Command(db, "SELECT chunk_id FROM chunk_versions_fts WHERE chunk_versions_fts MATCH $word", ("$word", word));
        var hits = new List<ChunkId>();
        await using System.Data.Common.DbDataReader reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            hits.Add(ChunkId.Parse(reader.GetString(0), null));

        return hits;
    }

    private MemoryIndexRebuilder Rebuilder(IEmbeddingGenerator<string, Embedding<float>> embeddings) =>
        new(_database.Factory, embeddings, new Users(), _database.Accessor, NullLogger<MemoryIndexRebuilder>.Instance);

    private async Task<ChunkId> WriteAsync(OwnerId owner, string title, string content)
    {
        using IDisposable scope = _database.Accessor.Use(TestExecutionContext.Text(owner));
        var draft = new ChunkDraft(ChunkScope.General, null, ChunkType.Fact, title, content);
        return (await MemoryTestKit.Store(_database, _old, _time).WriteAsync(draft, ChunkAuthor.User, Ct)).Value.Id;
    }

    private async Task SetCentroidAsync(ProjectId project)
    {
        await using MarshalDbContext db = _database.CreateContext();
        (await db.Projects.SingleAsync(candidate => candidate.Id == project, Ct)).Centroid = Vectors.ToBlob(new float[16]);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<(string, int)?> InfoAsync()
    {
        await using MarshalDbContext db = _database.CreateContext();
        await db.Database.OpenConnectionAsync(Ct);
        return await MemoryIndexes.ReadInfoAsync(db, Ct);
    }

    private sealed class Users : IUserDirectory
    {
        public Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OwnerId>>([TestExecutionContext.OwnerA, TestExecutionContext.OwnerB]);
    }

    private sealed class FailingAfterFirst(IEmbeddingGenerator<string, Embedding<float>> inner) : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _calls;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            ++_calls > 1 ? throw new HttpRequestException("Connection refused") : inner.GenerateAsync(values, options, cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);

        public void Dispose()
        {
        }
    }
}
