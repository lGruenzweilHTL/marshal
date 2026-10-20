using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Marshal.Memory.Consolidation;
using Marshal.Memory.Idle;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests.Idle;

public sealed class EfMemoryProposalsTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private TestDatabase _database = null!;
    private SqliteMemoryStore _store = null!;
    private EfMemoryProposals _proposals = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _store = MemoryTestKit.Store(_database, _embeddings, _time);
        _proposals = new EfMemoryProposals(_database.Factory, _store, _database.Accessor, _time);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task AcceptContradiction_ClosesTheOutdatedChunk_AuditNoLongerListsThePair()
    {
        (ChunkId old, ChunkId current) = await ContradictionAsync();
        DateTimeOffset march = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        Guid id = await AddAsync(MemoryProposalKind.Contradiction, old, current, new { current = current.Value, outdated = old.Value, closeAt = march, reason = "newer" });

        Result accepted = await _proposals.AcceptAsync(id, Ct);

        Assert.True(accepted.IsSuccess);
        Assert.Equal((DateTimeOffset?)march, (await _store.GetAsync(old, Ct))!.ValidTo);
        Assert.Null((await _store.GetAsync(current, Ct))!.ValidTo);
        Assert.Equal(MemoryProposalStatus.Accepted, (await StoredAsync(id)).Status);
        MemoryAuditReport audit = await new MemoryAudit(_database.Factory, Microsoft.Extensions.Options.Options.Create(new MemoryOptions()), _time, _proposals).RunAsync(Ct);
        Assert.Empty(audit.Contradictions);
        Assert.Empty(audit.Proposals);
    }

    [Fact]
    public async Task AcceptDuplicate_KeptChunkUpdatedWithAVersion_OtherSupersededAndClosed()
    {
        ChunkId coffee = await WriteAsync(ChunkType.Preference, "Coffee", "Likes oat milk.");
        ChunkId oat = await WriteAsync(ChunkType.Preference, "Coffee milk", "Prefers oat milk in coffee.");
        Guid id = await AddAsync(MemoryProposalKind.Duplicate, coffee, oat, new { keep = coffee.Value, other = oat.Value, title = "Coffee", content = "Prefers oat milk in coffee.", similarity = 0.95 });

        Assert.True((await _proposals.AcceptAsync(id, Ct)).IsSuccess);

        Assert.Equal("Prefers oat milk in coffee.", (await _store.GetAsync(coffee, Ct))!.Content);
        Assert.Equal("Likes oat milk.", Assert.Single(await _store.GetVersionsAsync(coffee, Ct)).Content);
        Assert.Contains(await _store.GetEdgesAsync(oat, Ct), edge => (edge.From, edge.To, edge.Kind) == (coffee, oat, ChunkEdgeKind.Supersedes));
        Assert.Equal((DateTimeOffset?)_time.GetUtcNow(), (await _store.GetAsync(oat, Ct))!.ValidTo);
    }

    [Fact]
    public async Task AcceptDuplicate_MergedTitleIsANewIdentity_NewChunkSupersedesBoth()
    {
        ChunkId coffee = await WriteAsync(ChunkType.Preference, "Coffee", "Likes oat milk.");
        ChunkId oat = await WriteAsync(ChunkType.Preference, "Coffee milk", "Prefers oat milk in coffee.");
        Guid id = await AddAsync(MemoryProposalKind.Duplicate, coffee, oat, new { keep = coffee.Value, other = oat.Value, title = "Coffee preference", content = "Oat milk in coffee.", similarity = 0.95 });

        Assert.True((await _proposals.AcceptAsync(id, Ct)).IsSuccess);

        ChunkId merged = ChunkIds.Compute(TestExecutionContext.OwnerA, ChunkScope.General, null, ChunkType.Preference, "Coffee preference");
        Assert.Equal("Oat milk in coffee.", (await _store.GetAsync(merged, Ct))!.Content);
        IReadOnlyList<MemoryEdge> edges = await _store.GetEdgesAsync(merged, Ct);
        Assert.Equal(new HashSet<ChunkId> { coffee, oat }, edges.Where(edge => edge.Kind == ChunkEdgeKind.Supersedes).Select(edge => edge.To).ToHashSet());
        Assert.NotNull((await _store.GetAsync(coffee, Ct))!.ValidTo);
        Assert.NotNull((await _store.GetAsync(oat, Ct))!.ValidTo);
    }

    [Fact]
    public async Task Dismiss_MemoryUnchanged_NotListedAnymore()
    {
        ChunkId coffee = await WriteAsync(ChunkType.Preference, "Coffee", "Likes oat milk.");
        ChunkId oat = await WriteAsync(ChunkType.Preference, "Coffee milk", "Prefers oat milk in coffee.");
        Guid id = await AddAsync(MemoryProposalKind.Duplicate, coffee, oat, new { keep = coffee.Value, other = oat.Value, title = "Coffee", content = "Merged.", similarity = 0.95 });
        Assert.Single(await _proposals.ListOpenAsync(Ct));

        Assert.True((await _proposals.DismissAsync(id, Ct)).IsSuccess);

        Assert.Empty(await _proposals.ListOpenAsync(Ct));
        Assert.Equal(("Likes oat milk.", (DateTimeOffset?)null), ((await _store.GetAsync(coffee, Ct))!.Content, (await _store.GetAsync(coffee, Ct))!.ValidTo));
        Assert.Null((await _store.GetAsync(oat, Ct))!.ValidTo);
        MemoryProposal stored = await StoredAsync(id);
        Assert.Equal((MemoryProposalStatus.Dismissed, (DateTimeOffset?)_time.GetUtcNow()), (stored.Status, stored.DecidedAt));
    }

    [Fact]
    public async Task List_OpenProposalsWithBothChunksAndTheSuggestion()
    {
        (ChunkId old, ChunkId current) = await ContradictionAsync();
        await AddAsync(MemoryProposalKind.Contradiction, old, current, new { current = current.Value, outdated = old.Value, closeAt = _time.GetUtcNow(), reason = "newer" });

        MemoryProposalView view = Assert.Single(await _proposals.ListOpenAsync(Ct));

        Assert.Equal(MemoryProposalKind.Contradiction, view.Kind);
        Assert.Equal(new HashSet<ChunkId> { old, current }, new HashSet<ChunkId> { view.A.Id, view.B.Id });
        Assert.Equal("newer", view.Suggestion.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Decided_AcceptAgain_Conflict_UnknownNotFound()
    {
        (ChunkId old, ChunkId current) = await ContradictionAsync();
        Guid id = await AddAsync(MemoryProposalKind.Contradiction, old, current, new { current = current.Value, outdated = old.Value, closeAt = _time.GetUtcNow(), reason = "newer" });
        await _proposals.DismissAsync(id, Ct);

        Assert.Equal("memory.proposal_decided", (await _proposals.AcceptAsync(id, Ct)).Error.Code);
        Assert.Equal("memory.proposal_not_found", (await _proposals.AcceptAsync(Guid.NewGuid(), Ct)).Error.Code);
    }

    [Fact]
    public async Task OtherOwner_CannotSeeOrDecide()
    {
        (ChunkId old, ChunkId current) = await ContradictionAsync();
        Guid id = await AddAsync(MemoryProposalKind.Contradiction, old, current, new { current = current.Value, outdated = old.Value, closeAt = _time.GetUtcNow(), reason = "newer" });
        _database.Accessor.Current = TestExecutionContext.Text(TestExecutionContext.OwnerB);

        Assert.Empty(await _proposals.ListOpenAsync(Ct));
        Assert.Equal("memory.proposal_not_found", (await _proposals.AcceptAsync(id, Ct)).Error.Code);
    }

    private async Task<(ChunkId Old, ChunkId Current)> ContradictionAsync()
    {
        ChunkId old = await WriteAsync(ChunkType.Device, "Voron nozzle", "0.4 mm nozzle.", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        ChunkId current = await WriteAsync(ChunkType.Device, "Voron nozzle (conflicting)", "0.6 mm nozzle.");
        await _store.LinkAsync(current, old, ChunkEdgeKind.Contradicts, Ct);
        return (old, current);
    }

    private async Task<ChunkId> WriteAsync(ChunkType type, string title, string content, DateTimeOffset? validFrom = null) =>
        (await _store.WriteAsync(new ChunkDraft(ChunkScope.General, null, type, title, content) { ValidFrom = validFrom }, ChunkAuthor.User, Ct)).Value.Id;

    private async Task<Guid> AddAsync(MemoryProposalKind kind, ChunkId first, ChunkId second, object suggestion)
    {
        (ChunkId a, ChunkId b) = MemoryProposal.Pair(first, second);
        var proposal = new MemoryProposal
        {
            Id = Guid.CreateVersion7(),
            OwnerId = TestExecutionContext.OwnerA,
            Kind = kind,
            ChunkA = a,
            ChunkB = b,
            SuggestionJson = JsonSerializer.Serialize(suggestion),
            Status = MemoryProposalStatus.Open,
            CreatedAt = _time.GetUtcNow(),
        };
        await using MarshalDbContext db = _database.CreateContext(TestExecutionContext.OwnerA);
        db.MemoryProposals.Add(proposal);
        await db.SaveChangesAsync(Ct);
        return proposal.Id;
    }

    private async Task<MemoryProposal> StoredAsync(Guid id)
    {
        await using MarshalDbContext db = _database.CreateContext(TestExecutionContext.OwnerA);
        return await db.MemoryProposals.SingleAsync(proposal => proposal.Id == id, Ct);
    }
}
