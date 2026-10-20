using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data.Memory;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class MemoryProposalTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _now = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Proposal_RoundTripsAndIsOwnerScoped()
    {
        (ChunkId a, ChunkId b) = await ChunksAsync(OwnerA);
        (ChunkId c, ChunkId d) = await ChunksAsync(OwnerB);
        await AddAsync(OwnerA, Proposal(OwnerA, MemoryProposalKind.Contradiction, a, b));
        await AddAsync(OwnerB, Proposal(OwnerB, MemoryProposalKind.Contradiction, c, d));

        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        MemoryProposal stored = Assert.Single(await context.MemoryProposals.ToListAsync(Ct));
        Assert.Equal((MemoryProposalKind.Contradiction, a, b, MemoryProposalStatus.Open, """{"current":"a"}""", _now, (DateTimeOffset?)null),
            (stored.Kind, stored.ChunkA, stored.ChunkB, stored.Status, stored.SuggestionJson, stored.CreatedAt, stored.DecidedAt));
    }

    [Fact]
    public async Task Proposal_SamePairAndKindAgain_RejectedEvenWhenDismissed()
    {
        (ChunkId a, ChunkId b) = await ChunksAsync(OwnerA);
        MemoryProposal dismissed = Proposal(OwnerA, MemoryProposalKind.Duplicate, a, b);
        dismissed.Status = MemoryProposalStatus.Dismissed;
        dismissed.DecidedAt = _now;
        await AddAsync(OwnerA, dismissed);

        await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(OwnerA, Proposal(OwnerA, MemoryProposalKind.Duplicate, a, b)));
    }

    [Fact]
    public async Task Proposal_SamePairOtherKind_Allowed()
    {
        (ChunkId a, ChunkId b) = await ChunksAsync(OwnerA);
        await AddAsync(OwnerA, Proposal(OwnerA, MemoryProposalKind.Duplicate, a, b));
        await AddAsync(OwnerA, Proposal(OwnerA, MemoryProposalKind.Contradiction, a, b));

        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        Assert.Equal(2, await context.MemoryProposals.CountAsync(Ct));
    }

    [Fact]
    public void Pair_OrderedSoTheIndexCatchesBothDirections()
    {
        var low = ChunkId.FromHash(new byte[32]);
        byte[] bytes = new byte[32];
        bytes[0] = 1;
        var high = ChunkId.FromHash(bytes);

        Assert.Equal((low, high), MemoryProposal.Pair(high, low));
        Assert.Equal((low, high), MemoryProposal.Pair(low, high));
    }

    private static MemoryProposal Proposal(OwnerId owner, MemoryProposalKind kind, ChunkId a, ChunkId b) => new()
    {
        Id = Guid.CreateVersion7(),
        OwnerId = owner,
        Kind = kind,
        ChunkA = a,
        ChunkB = b,
        SuggestionJson = """{"current":"a"}""",
        Status = MemoryProposalStatus.Open,
        CreatedAt = _now,
    };

    private async Task<(ChunkId, ChunkId)> ChunksAsync(OwnerId owner)
    {
        var ids = new List<ChunkId>();
        foreach (string title in new[] { "Nozzle 0.4", "Nozzle 0.6" })
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(owner + title));
            var id = ChunkId.FromHash(hash);
            await AddAsync(owner, new Chunk
            {
                Id = id,
                OwnerId = owner,
                Scope = ChunkScope.General,
                Type = ChunkType.Device,
                Title = title,
                Content = title,
                Importance = 3,
                CreatedBy = ChunkAuthor.User,
                CreatedAt = _now,
                UpdatedAt = _now,
                ValidFrom = _now,
            });
            ids.Add(id);
        }

        (ChunkId a, ChunkId b) = MemoryProposal.Pair(ids[0], ids[1]);
        return (a, b);
    }

    private async Task AddAsync<TEntity>(OwnerId owner, TEntity entity)
        where TEntity : class
    {
        await using MarshalDbContext context = _database.CreateContext(owner);
        context.Add(entity);
        await context.SaveChangesAsync(Ct);
    }
}
