using Marshal.Abstractions.Memory;

namespace Marshal.Memory.Retrieval;

/// <summary>
/// Scores retrieval candidates and picks the ones that fit the per-type quotas and the token budget.
/// </summary>
public interface IChunkScorer
{
    /// <summary>Candidates in descending score order, limited by quotas, budget and minimum score.</summary>
    IReadOnlyList<ScoredChunk> Select(IReadOnlyList<ChunkCandidate> candidates, ScoringContext context);
}

/// <summary>A retrieval candidate with its similarity to the query (0–1).</summary>
public sealed record ChunkCandidate(MemoryChunk Chunk, double Similarity);

/// <summary>A selected chunk.</summary>
public sealed record ScoredChunk(MemoryChunk Chunk, double Similarity, double Score);

/// <summary>What a selection depends on besides the candidates.</summary>
/// <param name="ActiveProjects">Projects active for this turn.</param>
/// <param name="Now">Reference time for freshness.</param>
/// <param name="TokenBudget">Estimated tokens the selection may use.</param>
public sealed record ScoringContext(IReadOnlySet<ProjectId> ActiveProjects, DateTimeOffset Now, int TokenBudget);
