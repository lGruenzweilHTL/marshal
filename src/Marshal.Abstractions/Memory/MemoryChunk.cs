namespace Marshal.Abstractions.Memory;

/// <summary>
/// A memory chunk as stored. Titles and content are English.
/// </summary>
/// <param name="Id">Deterministic id.</param>
/// <param name="Scope">General or project.</param>
/// <param name="ProjectId">Set if and only if the scope is project.</param>
/// <param name="Type">Kind of knowledge.</param>
/// <param name="Title">Short title; identity (normalized) together with owner, scope, project and type.</param>
/// <param name="Content">The knowledge itself.</param>
/// <param name="Importance">1 (trivia) to 5 (essential).</param>
/// <param name="Pinned">Always injected in full; changed only through the web UI / API.</param>
/// <param name="SourceRef">Where it came from (e.g. a note path), if any.</param>
/// <param name="CreatedBy">Who wrote it first.</param>
/// <param name="CreatedAt">First write.</param>
/// <param name="UpdatedAt">Last content change.</param>
/// <param name="DeletedAt">Soft-delete time; deleted chunks are never retrieved.</param>
/// <param name="LastRetrievedAt">Last time retrieval returned it.</param>
public sealed record MemoryChunk(
    ChunkId Id,
    ChunkScope Scope,
    ProjectId? ProjectId,
    ChunkType Type,
    string Title,
    string Content,
    int Importance,
    bool Pinned,
    string? SourceRef,
    ChunkAuthor CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    DateTimeOffset? LastRetrievedAt)
{
    /// <summary>Since when the fact is true in the world (defaults to the creation time).</summary>
    public DateTimeOffset ValidFrom { get; init; } = CreatedAt;

    /// <summary>Until when the fact was true; null while open. A chunk whose <see cref="ValidTo"/> is past is closed.</summary>
    public DateTimeOffset? ValidTo { get; init; }

    /// <summary>True when the fact stopped being true at or before <paramref name="now"/>.</summary>
    public bool IsClosedAt(DateTimeOffset now) => ValidTo is { } end && end <= now;
}

/// <summary>A chunk to write; its identity (scope, project, type, normalized title) determines the id.</summary>
/// <param name="Scope">General or project.</param>
/// <param name="ProjectId">Required for project scope, forbidden otherwise.</param>
/// <param name="Type">Kind of knowledge.</param>
/// <param name="Title">Short English title.</param>
/// <param name="Content">English content.</param>
/// <param name="Importance">1–5.</param>
/// <param name="SourceRef">Optional origin reference.</param>
public sealed record ChunkDraft(ChunkScope Scope, ProjectId? ProjectId, ChunkType Type, string Title, string Content, int Importance = 3, string? SourceRef = null)
{
    /// <summary>Since when the fact is true; null keeps the stored value, or means now for a new chunk or new content.</summary>
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>Until when the fact is true; null leaves it open. Must be after the valid-from time.</summary>
    public DateTimeOffset? ValidTo { get; init; }
}

/// <summary>What a write did.</summary>
public enum ChunkWriteOutcome
{
    Created,
    Updated,
    Unchanged,
}

/// <summary>Result of a chunk write.</summary>
/// <param name="Id">The addressed chunk.</param>
/// <param name="Outcome">Created, updated in place (previous content kept as a version) or unchanged.</param>
public sealed record ChunkWriteResult(ChunkId Id, ChunkWriteOutcome Outcome);

/// <summary>Previous content of a chunk, kept on a same-id update.</summary>
/// <param name="ChunkId">The chunk.</param>
/// <param name="Content">Content before the update.</param>
/// <param name="Importance">Importance before the update.</param>
/// <param name="SourceRef">Source before the update.</param>
/// <param name="ReplacedAt">When it was replaced.</param>
public sealed record MemoryChunkVersion(ChunkId ChunkId, string Content, int Importance, string? SourceRef, DateTimeOffset ReplacedAt)
{
    /// <summary>Since when this content was true.</summary>
    public DateTimeOffset ValidFrom { get; init; }

    /// <summary>Until when this content was true.</summary>
    public DateTimeOffset? ValidTo { get; init; }
}

/// <summary>A relation between two chunks.</summary>
/// <param name="From">Source chunk.</param>
/// <param name="To">Target chunk.</param>
/// <param name="Kind">Relation.</param>
/// <param name="CreatedAt">When it was added.</param>
public sealed record MemoryEdge(ChunkId From, ChunkId To, ChunkEdgeKind Kind, DateTimeOffset CreatedAt);
