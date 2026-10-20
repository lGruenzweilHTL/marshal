namespace Marshal.Abstractions.Memory;

/// <summary>
/// Hybrid memory search for the current owner: vector and keyword seeds, edge expansion, superseded chunks replaced
/// by their successors, scored and limited by quotas and a token budget. Soft-deleted chunks are never returned, closed
/// ones only for a search at a past time.
/// </summary>
public interface IMemoryRetriever
{
    /// <summary>Relevant chunks for a query.</summary>
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(MemoryQuery query, CancellationToken cancellationToken);

    /// <summary>All live pinned chunks (always injected in full).</summary>
    Task<IReadOnlyList<MemoryChunk>> GetPinnedAsync(CancellationToken cancellationToken);
}

/// <summary>A memory search.</summary>
/// <param name="Text">Query text (turn text or recall query).</param>
/// <param name="Embedding">Embedding of <paramref name="Text"/> if already computed (the turn embedding).</param>
/// <param name="ActiveProjects">Projects active for this turn (boosted); others are penalized, never excluded.</param>
/// <param name="TokenBudget">Estimated tokens the result may use.</param>
/// <param name="Type">Only chunks of this type.</param>
/// <param name="Project">Only chunks of this project.</param>
/// <param name="IncludePinned">Also return pinned chunks (they are injected separately in automatic retrieval).</param>
/// <param name="MarkRetrieved">Set the chunks' last-retrieved time; off for maintenance reads that must not keep chunks fresh.</param>
/// <param name="AsOf">
/// Search what was valid at this time: closed chunks and earlier versions whose valid time covers it, each returned with
/// that content and valid time. Without it, only chunks not closed now are searched.
/// </param>
public sealed record MemoryQuery(
    string Text,
    ReadOnlyMemory<float>? Embedding,
    IReadOnlySet<ProjectId> ActiveProjects,
    int TokenBudget,
    ChunkType? Type = null,
    ProjectId? Project = null,
    bool IncludePinned = false,
    bool MarkRetrieved = true,
    DateTimeOffset? AsOf = null);

/// <summary>How a chunk was found.</summary>
public enum RetrievalSource
{
    Vector,
    Keyword,
    Edge,
    Successor,
}

/// <summary>A retrieved chunk.</summary>
/// <param name="Chunk">The chunk.</param>
/// <param name="Similarity">Similarity to the query (0–1).</param>
/// <param name="Score">Final score.</param>
/// <param name="Source">How it was found.</param>
public sealed record RetrievedChunk(MemoryChunk Chunk, double Similarity, double Score, RetrievalSource Source);
