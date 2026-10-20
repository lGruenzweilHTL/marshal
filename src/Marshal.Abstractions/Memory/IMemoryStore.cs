namespace Marshal.Abstractions.Memory;

/// <summary>
/// The current owner's memory chunks: writes through deterministic ids, versions, edges, soft deletes. Every write
/// updates the keyword and vector indexes in the same transaction.
/// </summary>
public interface IMemoryStore
{
    /// <summary>A chunk by id, including soft-deleted ones.</summary>
    Task<MemoryChunk?> GetAsync(ChunkId id, CancellationToken cancellationToken);

    /// <summary>Creates the chunk or updates it in place (previous content kept as a version). Never pins.</summary>
    Task<Result<ChunkWriteResult>> WriteAsync(ChunkDraft draft, ChunkAuthor author, CancellationToken cancellationToken);

    /// <summary>Writes a chunk with a new identity and a <c>supersedes</c> edge from it to <paramref name="old"/>.</summary>
    Task<Result<ChunkWriteResult>> SupersedeAsync(ChunkId old, ChunkDraft replacement, ChunkAuthor author, CancellationToken cancellationToken);

    /// <summary>
    /// Ends a chunk's valid time at <paramref name="validTo"/> without a new version. A time not after the chunk's
    /// valid-from time moves that to just before it, so the range stays valid.
    /// </summary>
    Task<Result> CloseAsync(ChunkId id, DateTimeOffset validTo, CancellationToken cancellationToken);

    /// <summary>Sets a chunk's valid time exactly (an open end clears <c>validTo</c>), without a new version.</summary>
    Task<Result> SetValidTimeAsync(ChunkId id, DateTimeOffset validFrom, DateTimeOffset? validTo, CancellationToken cancellationToken);

    /// <summary>Adds an edge; <c>supersedes</c> never points from a chunk to itself.</summary>
    Task<Result> LinkAsync(ChunkId from, ChunkId to, ChunkEdgeKind kind, CancellationToken cancellationToken);

    /// <summary>Soft-deletes a chunk and removes it from the indexes.</summary>
    Task<Result> DeleteAsync(ChunkId id, CancellationToken cancellationToken);

    /// <summary>Pins or unpins a chunk. Only the web UI / API may call this; no tool does.</summary>
    Task<Result> SetPinnedAsync(ChunkId id, bool pinned, CancellationToken cancellationToken);

    /// <summary>Previous versions of a chunk, oldest first.</summary>
    Task<IReadOnlyList<MemoryChunkVersion>> GetVersionsAsync(ChunkId id, CancellationToken cancellationToken);

    /// <summary>Edges from or to a chunk.</summary>
    Task<IReadOnlyList<MemoryEdge>> GetEdgesAsync(ChunkId id, CancellationToken cancellationToken);
}
