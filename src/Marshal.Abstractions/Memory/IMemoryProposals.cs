using System.Text.Json;

namespace Marshal.Abstractions.Memory;

/// <summary>
/// The current owner's memory proposals from the idle-time jobs. Only a user action decides one: accepting applies the
/// suggestion through the memory store, dismissing changes nothing and keeps the pair from being proposed again.
/// </summary>
public interface IMemoryProposals
{
    /// <summary>Open proposals whose chunks are both live, oldest first.</summary>
    Task<IReadOnlyList<MemoryProposalView>> ListOpenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies the suggestion: a contradiction closes the outdated chunk; a duplicate updates the kept chunk with the
    /// merged text and supersedes the other. Fails with <c>memory.proposal_not_found</c> or <c>memory.proposal_decided</c>.
    /// </summary>
    Task<Result> AcceptAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Marks the proposal dismissed without changing memory.</summary>
    Task<Result> DismissAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>An open memory proposal.</summary>
/// <param name="Id">Proposal id.</param>
/// <param name="Kind">Contradiction or duplicate.</param>
/// <param name="A">The first chunk of the pair.</param>
/// <param name="B">The second chunk.</param>
/// <param name="Suggestion">
/// Contradiction: <c>{ current, outdated, closeAt, reason }</c>; duplicate: <c>{ keep, other, title, content, similarity }</c>.
/// </param>
/// <param name="CreatedAt">When it was proposed.</param>
public sealed record MemoryProposalView(Guid Id, MemoryProposalKind Kind, MemoryChunk A, MemoryChunk B, JsonElement Suggestion, DateTimeOffset CreatedAt);
