using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;

namespace Marshal.Data.Memory;

/// <summary>
/// A memory chunk. Its embedding lives in the <c>chunk_vectors</c> sqlite-vec table, its keyword entry in
/// <c>chunks_fts</c>; both are maintained by the memory store in the same transaction.
/// </summary>
public sealed class Chunk : IOwned
{
    public required ChunkId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required ChunkScope Scope { get; init; }

    /// <summary>Set if and only if the scope is project.</summary>
    public ProjectId? ProjectId { get; init; }

    public required ChunkType Type { get; init; }

    public required string Title { get; set; }

    public required string Content { get; set; }

    public int Importance { get; set; }

    public bool Pinned { get; set; }

    public string? SourceRef { get; set; }

    public required ChunkAuthor CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset? LastRetrievedAt { get; set; }
}

/// <summary>Previous content of a chunk after a same-id update.</summary>
public sealed class ChunkVersion : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required ChunkId ChunkId { get; init; }

    public required string Content { get; init; }

    public required int Importance { get; init; }

    public string? SourceRef { get; init; }

    public required DateTimeOffset ReplacedAt { get; init; }
}

/// <summary>A relation between two chunks of the same owner.</summary>
public sealed class ChunkEdge : IOwned
{
    public required ChunkId FromId { get; init; }

    public required ChunkId ToId { get; init; }

    public required ChunkEdgeKind Kind { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
