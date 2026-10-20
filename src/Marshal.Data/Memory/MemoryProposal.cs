using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;

namespace Marshal.Data.Memory;

/// <summary>
/// A change to memory prepared by an idle-time job, waiting for the user. The pair is stored ordered
/// (<see cref="Pair"/>), and one row per owner, kind and pair is allowed, so a dismissed pair is never proposed again.
/// </summary>
public sealed class MemoryProposal : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required MemoryProposalKind Kind { get; init; }

    /// <summary>The lower chunk id of the pair (ordinal order).</summary>
    public required ChunkId ChunkA { get; init; }

    /// <summary>The higher chunk id of the pair.</summary>
    public required ChunkId ChunkB { get; init; }

    /// <summary>The suggestion as JSON; its shape depends on <see cref="Kind"/>.</summary>
    public required string SuggestionJson { get; init; }

    public required MemoryProposalStatus Status { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>A chunk pair in stored order.</summary>
    public static (ChunkId A, ChunkId B) Pair(ChunkId first, ChunkId second) =>
        string.CompareOrdinal(first.Value, second.Value) <= 0 ? (first, second) : (second, first);
}
