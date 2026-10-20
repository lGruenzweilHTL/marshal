using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Memory.Idle;

/// <summary>
/// <see cref="IMemoryProposals"/> on the <c>memory_proposals</c> table (owner-scoped). Accepting applies the suggestion
/// through <see cref="IMemoryStore"/> as the user, so versions record the change; the API audits the decision.
/// </summary>
public sealed class EfMemoryProposals(
    IDbContextFactory<MarshalDbContext> contexts,
    IMemoryStore store,
    IExecutionContextAccessor executionContext,
    TimeProvider timeProvider) : IMemoryProposals
{
    public async Task<IReadOnlyList<MemoryProposalView>> ListOpenAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<MemoryProposal> open = await db.MemoryProposals.AsNoTracking()
            .Where(proposal => proposal.Status == MemoryProposalStatus.Open)
            .OrderBy(proposal => proposal.CreatedAt)
            .ToListAsync(cancellationToken);
        List<ChunkId> ids = [.. open.SelectMany(proposal => new[] { proposal.ChunkA, proposal.ChunkB }).Distinct()];
        var chunks = (await db.Chunks.AsNoTracking().Where(chunk => ids.Contains(chunk.Id) && chunk.DeletedAt == null).ToListAsync(cancellationToken))
            .ToDictionary(chunk => chunk.Id);

        return [.. open
            .Where(proposal => chunks.ContainsKey(proposal.ChunkA) && chunks.ContainsKey(proposal.ChunkB))
            .Select(proposal => new MemoryProposalView(
                proposal.Id,
                proposal.Kind,
                SqliteMemoryStore.ToView(chunks[proposal.ChunkA]),
                SqliteMemoryStore.ToView(chunks[proposal.ChunkB]),
                JsonDocument.Parse(proposal.SuggestionJson).RootElement.Clone(),
                proposal.CreatedAt))];
    }

    public async Task<Result> AcceptAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<MemoryProposal> found = await OpenAsync(id, cancellationToken);
        if (found.IsFailure)
            return found.Error;

        MemoryProposal proposal = found.Value;
        JsonElement suggestion = JsonDocument.Parse(proposal.SuggestionJson).RootElement;
        Result applied = proposal.Kind == MemoryProposalKind.Contradiction
            ? await AcceptContradictionAsync(suggestion, cancellationToken)
            : await AcceptDuplicateAsync(suggestion, cancellationToken);
        return applied.IsFailure ? applied.Error : await DecideAsync(id, MemoryProposalStatus.Accepted, cancellationToken);
    }

    public async Task<Result> DismissAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<MemoryProposal> found = await OpenAsync(id, cancellationToken);
        return found.IsFailure ? found.Error : await DecideAsync(id, MemoryProposalStatus.Dismissed, cancellationToken);
    }

    private async Task<Result> AcceptContradictionAsync(JsonElement suggestion, CancellationToken cancellationToken)
    {
        if (Chunk(suggestion, "outdated") is not { } outdated || !suggestion.TryGetProperty("closeAt", out JsonElement closeAt) || !closeAt.TryGetDateTimeOffset(out DateTimeOffset at))
            return Invalid();

        return await store.CloseAsync(outdated, at, cancellationToken);
    }

    // The kept chunk takes the merged text: in place when the merged title keeps its identity, else through a
    // successor. The other chunk is superseded by the result and closed.
    private async Task<Result> AcceptDuplicateAsync(JsonElement suggestion, CancellationToken cancellationToken)
    {
        if (Chunk(suggestion, "keep") is not { } keepId || Chunk(suggestion, "other") is not { } otherId
            || Text(suggestion, "title") is not { } title || Text(suggestion, "content") is not { } content
            || await store.GetAsync(keepId, cancellationToken) is not { DeletedAt: null } keep)
        {
            return Invalid();
        }

        var draft = new ChunkDraft(keep.Scope, keep.ProjectId, keep.Type, title, content, keep.Importance, keep.SourceRef);
        OwnerId owner = executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Memory accessed without an execution context.");
        bool sameIdentity = ChunkIds.Compute(owner, keep.Scope, keep.ProjectId, keep.Type, title) == keepId;
        Result<ChunkWriteResult> written = sameIdentity
            ? await store.WriteAsync(draft with { ValidFrom = keep.ValidFrom }, ChunkAuthor.User, cancellationToken)
            : await store.SupersedeAsync(keepId, draft, ChunkAuthor.User, cancellationToken);
        if (written.IsFailure)
            return written.Error;

        Result linked = await store.LinkAsync(written.Value.Id, otherId, ChunkEdgeKind.Supersedes, cancellationToken);
        return linked.IsFailure ? linked : await store.CloseAsync(otherId, timeProvider.GetUtcNow(), cancellationToken);
    }

    private async Task<Result<MemoryProposal>> OpenAsync(Guid id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        MemoryProposal? proposal = await db.MemoryProposals.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (proposal is null)
            return new Error("memory.proposal_not_found", "The memory proposal does not exist.", ErrorType.NotFound);

        return proposal.Status == MemoryProposalStatus.Open
            ? proposal
            : new Error("memory.proposal_decided", "The memory proposal was already decided.", ErrorType.Conflict);
    }

    private async Task<Result> DecideAsync(Guid id, MemoryProposalStatus status, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        await db.MemoryProposals.Where(proposal => proposal.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(proposal => proposal.Status, status)
            .SetProperty(proposal => proposal.DecidedAt, now), cancellationToken);
        return Result.Success();
    }

    private static ChunkId? Chunk(JsonElement suggestion, string name) =>
        ChunkId.TryParse(Text(suggestion, name), null, out ChunkId id) ? id : null;

    private static string? Text(JsonElement suggestion, string name) =>
        suggestion.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static Error Invalid() => new("memory.proposal_invalid", "The memory proposal's suggestion cannot be applied.", ErrorType.Failure);
}
