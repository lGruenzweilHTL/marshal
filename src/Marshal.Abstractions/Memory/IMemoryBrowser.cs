namespace Marshal.Abstractions.Memory;

/// <summary>
/// Read side of the memory view: chunk lists with search and filters, and the history of a chunk. Browsing never
/// marks chunks as retrieved.
/// </summary>
public interface IMemoryBrowser
{
    Task<IReadOnlyList<MemoryChunk>> ListAsync(MemoryListFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Predecessors (through <c>supersedes</c>), their and the chunk's earlier versions and the current content,
    /// oldest first; null when the chunk does not exist.
    /// </summary>
    Task<IReadOnlyList<MemoryHistoryEntry>?> GetHistoryAsync(ChunkId id, CancellationToken cancellationToken);
}

/// <summary>Filters of the chunk list.</summary>
/// <param name="Query">Hybrid search text (vector and keyword), ranked by relevance; null lists newest first.</param>
/// <param name="Type">Only this type.</param>
/// <param name="Scope">Only this scope.</param>
/// <param name="Project">Only chunks of this project.</param>
/// <param name="Pinned">Only pinned (true) or unpinned (false) chunks.</param>
/// <param name="IncludeDeleted">Also soft-deleted chunks (never with a query).</param>
/// <param name="Limit">Maximum number of chunks.</param>
/// <param name="Closed">Only closed (true) or not closed (false) chunks; without a query.</param>
public sealed record MemoryListFilter(
    string? Query = null,
    ChunkType? Type = null,
    ChunkScope? Scope = null,
    ProjectId? Project = null,
    bool? Pinned = null,
    bool IncludeDeleted = false,
    int Limit = 100,
    bool? Closed = null);

/// <summary>Where a history entry comes from.</summary>
public enum MemoryHistoryKind
{
    /// <summary>The current content of a chunk that was superseded (directly or further back).</summary>
    Predecessor,

    /// <summary>An earlier version of a chunk, replaced by a same-id update.</summary>
    Version,

    /// <summary>The chunk's current content.</summary>
    Current,
}

/// <summary>One state in a chunk's history.</summary>
/// <param name="ChunkId">The chunk the state belongs to.</param>
/// <param name="Kind">Predecessor, earlier version or current.</param>
/// <param name="Title">Title of that chunk.</param>
/// <param name="Content">Content in that state.</param>
/// <param name="Importance">Importance in that state.</param>
/// <param name="ValidFrom">Since when the state was true.</param>
/// <param name="ValidTo">Until when it was true; null while open.</param>
public sealed record MemoryHistoryEntry(ChunkId ChunkId, MemoryHistoryKind Kind, string Title, string Content, int Importance, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo = null);
