using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Memory.Idle;

/// <summary>Writes open memory proposals; a pair that was ever proposed for the kind is skipped.</summary>
internal static class ProposalWriter
{
    public static async Task<bool> AddAsync(
        IDbContextFactory<MarshalDbContext> contexts, OwnerId owner, MemoryProposalKind kind, ChunkId first, ChunkId second, string suggestion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        (ChunkId a, ChunkId b) = MemoryProposal.Pair(first, second);
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await db.MemoryProposals.AnyAsync(proposal => proposal.Kind == kind && proposal.ChunkA == a && proposal.ChunkB == b, cancellationToken))
            return false;

        db.MemoryProposals.Add(new MemoryProposal
        {
            Id = Guid.CreateVersion7(),
            OwnerId = owner,
            Kind = kind,
            ChunkA = a,
            ChunkB = b,
            SuggestionJson = suggestion,
            Status = MemoryProposalStatus.Open,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
