using Marshal.Abstractions;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests;

public sealed class SqliteMemoryStoreTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private TestDatabase _database = null!;
    private SqliteMemoryStore _store = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _store = MemoryTestKit.Store(_database, _embeddings, _time);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Write_ProjectScopeWithoutProject_ValidationError()
    {
        Result<ChunkWriteResult> result = await _store.WriteAsync(new ChunkDraft(ChunkScope.Project, null, ChunkType.Fact, "Nozzle", "0.6 mm"), ChunkAuthor.Agent, Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("memory.invalid_chunk", result.Error.Code);
    }

    [Theory]
    [InlineData("", "content", 3)]
    [InlineData("Title", " ", 3)]
    [InlineData("Title", "content", 0)]
    [InlineData("Title", "content", 6)]
    public async Task Write_InvalidFields_ValidationError(string title, string content, int importance)
    {
        Result<ChunkWriteResult> result = await _store.WriteAsync(new ChunkDraft(ChunkScope.General, null, ChunkType.Fact, title, content, importance), ChunkAuthor.Agent, Ct);

        Assert.Equal("memory.invalid_chunk", result.Error.Code);
    }

    [Fact]
    public async Task Write_UnknownProject_NotFound()
    {
        Result<ChunkWriteResult> result = await _store.WriteAsync(new ChunkDraft(ChunkScope.Project, ProjectId.New(), ChunkType.Fact, "Nozzle", "0.6 mm"), ChunkAuthor.Agent, Ct);

        Assert.Equal("memory.project_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Write_New_CreatedAndIndexed()
    {
        Result<ChunkWriteResult> result = await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct);

        Assert.Equal(ChunkWriteOutcome.Created, result.Value.Outcome);
        MemoryChunk chunk = (await _store.GetAsync(result.Value.Id, Ct))!;
        Assert.Equal(("Voron nozzle size", "0.4 mm", ChunkAuthor.Agent, false), (chunk.Title, chunk.Content, chunk.CreatedBy, chunk.Pinned));
        Assert.Equal([result.Value.Id], await MemoryTestKit.KeywordHitsAsync(_database, "nozzle"));
        Assert.Equal(await MemoryTestKit.EmbedAsync(_embeddings, "Voron nozzle size\n0.4 mm"), await MemoryTestKit.StoredVectorAsync(_database, result.Value.Id));
    }

    [Fact]
    public async Task Write_SameIdNewContent_UpdatesInPlaceAndKeepsVersion()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        _time.Advance(TimeSpan.FromDays(1));

        Result<ChunkWriteResult> update = await _store.WriteAsync(Nozzle("0.6 mm") with { Title = "voron   Nozzle size" }, ChunkAuthor.Agent, Ct);

        Assert.Equal(new ChunkWriteResult(id, ChunkWriteOutcome.Updated), update.Value);
        MemoryChunk chunk = (await _store.GetAsync(id, Ct))!;
        Assert.Equal("0.6 mm", chunk.Content);
        Assert.Equal(_time.GetUtcNow(), chunk.UpdatedAt);
        MemoryChunkVersion version = Assert.Single(await _store.GetVersionsAsync(id, Ct));
        Assert.Equal(("0.4 mm", 3, _time.GetUtcNow()), (version.Content, version.Importance, version.ReplacedAt));
        Assert.Equal([id], await MemoryTestKit.KeywordHitsAsync(_database, "0.6"));
        Assert.Empty(await MemoryTestKit.KeywordHitsAsync(_database, "0.4"));
        Assert.Equal(await MemoryTestKit.EmbedAsync(_embeddings, "voron   Nozzle size\n0.6 mm"), await MemoryTestKit.StoredVectorAsync(_database, id));
    }

    [Fact]
    public async Task Write_SameContent_UnchangedWithoutVersion()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        int embedded = _embeddings.CallCount;

        Result<ChunkWriteResult> again = await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Consolidation, Ct);

        Assert.Equal(ChunkWriteOutcome.Unchanged, again.Value.Outcome);
        Assert.Empty(await _store.GetVersionsAsync(id, Ct));
        Assert.Equal(embedded, _embeddings.CallCount);
    }

    [Fact]
    public async Task Write_NeverPinsAndKeepsPin()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        await _store.SetPinnedAsync(id, pinned: true, Ct);

        await _store.WriteAsync(Nozzle("0.6 mm"), ChunkAuthor.Agent, Ct);

        Assert.True((await _store.GetAsync(id, Ct))!.Pinned);
    }

    [Fact]
    public async Task Supersede_MoveToProject_NewChunkWithEdgeToOld()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron");
        ChunkId old = (await _store.WriteAsync(Nozzle("0.6 mm"), ChunkAuthor.Agent, Ct)).Value.Id;

        Result<ChunkWriteResult> moved = await _store.SupersedeAsync(old, Nozzle("0.6 mm") with { Scope = ChunkScope.Project, ProjectId = voron }, ChunkAuthor.User, Ct);

        Assert.Equal(ChunkWriteOutcome.Created, moved.Value.Outcome);
        Assert.NotEqual(old, moved.Value.Id);
        Assert.Equal(voron, (await _store.GetAsync(moved.Value.Id, Ct))!.ProjectId);
        MemoryEdge edge = Assert.Single(await _store.GetEdgesAsync(old, Ct));
        Assert.Equal((moved.Value.Id, old, ChunkEdgeKind.Supersedes), (edge.From, edge.To, edge.Kind));
    }

    [Fact]
    public async Task Supersede_SameIdentity_Rejected()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;

        Result<ChunkWriteResult> result = await _store.SupersedeAsync(id, Nozzle("0.6 mm"), ChunkAuthor.User, Ct);

        Assert.Equal("memory.self_supersede", result.Error.Code);
        Assert.Equal("0.4 mm", (await _store.GetAsync(id, Ct))!.Content);
    }

    [Fact]
    public async Task Link_SelfSupersede_Rejected_OtherEdgesStored()
    {
        ChunkId a = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        ChunkId b = (await _store.WriteAsync(Nozzle("0.6 mm") with { Title = "Voron nozzle" }, ChunkAuthor.Agent, Ct)).Value.Id;

        Assert.Equal("memory.self_supersede", (await _store.LinkAsync(a, a, ChunkEdgeKind.Supersedes, Ct)).Error.Code);
        Assert.True((await _store.LinkAsync(a, b, ChunkEdgeKind.Contradicts, Ct)).IsSuccess);
        Assert.True((await _store.LinkAsync(a, b, ChunkEdgeKind.Contradicts, Ct)).IsSuccess);
        Assert.Equal("memory.chunk_not_found", (await _store.LinkAsync(a, ChunkIds.Compute(TestExecutionContext.OwnerA, ChunkScope.General, null, ChunkType.Fact, "x"), ChunkEdgeKind.RelatesTo, Ct)).Error.Code);

        Assert.Single(await _store.GetEdgesAsync(b, Ct));
    }

    [Fact]
    public async Task Delete_SoftDeletesAndRemovesFromIndexes()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;

        Assert.True((await _store.DeleteAsync(id, Ct)).IsSuccess);

        Assert.Equal(_time.GetUtcNow(), (await _store.GetAsync(id, Ct))!.DeletedAt);
        Assert.Empty(await MemoryTestKit.KeywordHitsAsync(_database, "nozzle"));
        Assert.Null(await MemoryTestKit.StoredVectorAsync(_database, id));
    }

    [Fact]
    public async Task Write_DeletedChunkAgain_Restored()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        await _store.DeleteAsync(id, Ct);

        await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct);

        Assert.Null((await _store.GetAsync(id, Ct))!.DeletedAt);
        Assert.Equal([id], await MemoryTestKit.KeywordHitsAsync(_database, "nozzle"));
    }

    [Fact]
    public async Task Write_IndexFailure_RollsBackChunkAndKeywordIndex()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        SqliteMemoryStore otherModel = MemoryTestKit.Store(_database, new FakeEmbeddingGenerator(dimensions: 8), _time);

        await Assert.ThrowsAnyAsync<Exception>(() => otherModel.WriteAsync(Nozzle("0.6 mm"), ChunkAuthor.Agent, Ct));

        Assert.Equal("0.4 mm", (await _store.GetAsync(id, Ct))!.Content);
        Assert.Empty(await _store.GetVersionsAsync(id, Ct));
        Assert.Empty(await MemoryTestKit.KeywordHitsAsync(_database, "0.6"));
    }

    [Fact]
    public async Task OtherOwner_SeesNothingAndGetsOwnId()
    {
        ChunkId mine = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        _database.Accessor.Current = TestExecutionContext.Text(TestExecutionContext.OwnerB);

        Assert.Null(await _store.GetAsync(mine, Ct));
        Assert.Empty(await MemoryTestKit.KeywordHitsAsync(_database, "nozzle"));
        Assert.Equal("memory.chunk_not_found", (await _store.DeleteAsync(mine, Ct)).Error.Code);
        ChunkId theirs = (await _store.WriteAsync(Nozzle("0.8 mm"), ChunkAuthor.Agent, Ct)).Value.Id;

        Assert.NotEqual(mine, theirs);
        _database.Accessor.Current = TestExecutionContext.Text();
        Assert.Equal("0.4 mm", (await _store.GetAsync(mine, Ct))!.Content);
    }

    [Fact]
    public async Task RenameProject_KeepsChunkIds()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron");
        ChunkId id = (await _store.WriteAsync(Nozzle("0.6 mm") with { Scope = ChunkScope.Project, ProjectId = voron }, ChunkAuthor.Agent, Ct)).Value.Id;
        await using (MarshalDbContext db = _database.CreateContext())
        {
            Project project = db.Projects.Single();
            project.Name = "Voron 2.4";
            await db.SaveChangesAsync(Ct);
        }

        Result<ChunkWriteResult> again = await _store.WriteAsync(Nozzle("0.6 mm") with { Scope = ChunkScope.Project, ProjectId = voron }, ChunkAuthor.Agent, Ct);

        Assert.Equal(new ChunkWriteResult(id, ChunkWriteOutcome.Unchanged), again.Value);
    }

    [Fact]
    public async Task Write_WithoutValidTime_ValidFromCreationOpenEnded()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;

        MemoryChunk chunk = (await _store.GetAsync(id, Ct))!;
        Assert.Equal((_time.GetUtcNow(), (DateTimeOffset?)null), (chunk.ValidFrom, chunk.ValidTo));
    }

    [Fact]
    public async Task Write_ValidToNotAfterValidFrom_ValidationError()
    {
        DateTimeOffset from = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Result<ChunkWriteResult> result = await _store.WriteAsync(Nozzle("0.4 mm") with { ValidFrom = from, ValidTo = from.AddDays(-1) }, ChunkAuthor.Agent, Ct);

        Assert.Equal("memory.invalid_chunk", result.Error.Code);
    }

    [Fact]
    public async Task Write_SameIdUpdate_VersionClosedAtNewValidFrom()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm") with { ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }, ChunkAuthor.Agent, Ct)).Value.Id;
        _time.Advance(TimeSpan.FromDays(1));
        DateTimeOffset march = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        await _store.WriteAsync(Nozzle("0.6 mm") with { ValidFrom = march }, ChunkAuthor.Agent, Ct);

        MemoryChunk chunk = (await _store.GetAsync(id, Ct))!;
        Assert.Equal(("0.6 mm", march, (DateTimeOffset?)null), (chunk.Content, chunk.ValidFrom, chunk.ValidTo));
        MemoryChunkVersion version = Assert.Single(await _store.GetVersionsAsync(id, Ct));
        Assert.Equal(("0.4 mm", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), (DateTimeOffset?)march), (version.Content, version.ValidFrom, version.ValidTo));
    }

    [Fact]
    public async Task Write_SameIdUpdateWithoutValidTime_VersionClosedNow()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        _time.Advance(TimeSpan.FromDays(1));

        await _store.WriteAsync(Nozzle("0.6 mm"), ChunkAuthor.Agent, Ct);

        Assert.Equal(_time.GetUtcNow(), (await _store.GetAsync(id, Ct))!.ValidFrom);
        Assert.Equal(_time.GetUtcNow(), Assert.Single(await _store.GetVersionsAsync(id, Ct)).ValidTo);
    }

    [Fact]
    public async Task Write_OnlyValidToChanged_ClosesWithoutVersion()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        DateTimeOffset end = _time.GetUtcNow().AddDays(2);

        Result<ChunkWriteResult> closed = await _store.WriteAsync(Nozzle("0.4 mm") with { ValidTo = end }, ChunkAuthor.User, Ct);

        Assert.Equal(ChunkWriteOutcome.Updated, closed.Value.Outcome);
        MemoryChunk chunk = (await _store.GetAsync(id, Ct))!;
        Assert.Equal((_time.GetUtcNow(), (DateTimeOffset?)end), (chunk.ValidFrom, chunk.ValidTo));
        Assert.Empty(await _store.GetVersionsAsync(id, Ct));
    }

    [Fact]
    public async Task Supersede_OpenOldChunk_ClosedAtNewValidFrom()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron");
        ChunkId old = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        _time.Advance(TimeSpan.FromDays(1));

        ChunkId moved = (await _store.SupersedeAsync(old, Nozzle("0.4 mm") with { Scope = ChunkScope.Project, ProjectId = voron }, ChunkAuthor.User, Ct)).Value.Id;

        Assert.Equal(_time.GetUtcNow(), (await _store.GetAsync(old, Ct))!.ValidTo);
        Assert.Equal(_time.GetUtcNow(), (await _store.GetAsync(moved, Ct))!.ValidFrom);
    }

    [Fact]
    public async Task Close_SetsValidToWithoutVersion()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm") with { ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }, ChunkAuthor.Agent, Ct)).Value.Id;
        DateTimeOffset march = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Result closed = await _store.CloseAsync(id, march, Ct);

        Assert.True(closed.IsSuccess);
        Assert.Equal((DateTimeOffset?)march, (await _store.GetAsync(id, Ct))!.ValidTo);
        Assert.Empty(await _store.GetVersionsAsync(id, Ct));
    }

    [Fact]
    public async Task Close_BeforeValidFrom_ValidTimeEndsThereAndStaysAValidRange()
    {
        ChunkId id = (await _store.WriteAsync(Nozzle("0.4 mm"), ChunkAuthor.Agent, Ct)).Value.Id;
        DateTimeOffset march = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        await _store.CloseAsync(id, march, Ct);

        MemoryChunk chunk = (await _store.GetAsync(id, Ct))!;
        Assert.Equal((DateTimeOffset?)march, chunk.ValidTo);
        Assert.True(chunk.ValidFrom < march);
    }

    [Fact]
    public async Task Close_UnknownChunk_NotFound()
    {
        Assert.Equal("memory.chunk_not_found", (await _store.CloseAsync(ChunkId.FromHash(new byte[32]), _time.GetUtcNow(), Ct)).Error.Code);
    }

    private static ChunkDraft Nozzle(string content) => new(ChunkScope.General, null, ChunkType.Device, "Voron nozzle size", content);
}
