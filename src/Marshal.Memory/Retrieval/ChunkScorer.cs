using Marshal.Abstractions.Memory;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Retrieval;

/// <summary>
/// score = similarity × importance / 5 × freshness × project factor, with freshness = 0.5^(age / half-life of the type).
/// Chunks of active projects are boosted, of inactive projects penalized, never excluded.
/// </summary>
public sealed class ChunkScorer(IOptions<MemoryOptions> options) : IChunkScorer
{
    public IReadOnlyList<ScoredChunk> Select(IReadOnlyList<ChunkCandidate> candidates, ScoringContext context)
    {
        RetrievalOptions retrieval = options.Value.Retrieval;
        var perType = new Dictionary<ChunkType, int>();
        var selected = new List<ScoredChunk>();
        int tokens = 0;
        foreach (ScoredChunk scored in candidates.Select(candidate => Score(candidate, context, retrieval)).OrderByDescending(scored => scored.Score))
        {
            if (scored.Score < retrieval.MinScore)
                break;

            ChunkType type = scored.Chunk.Type;
            int used = perType.GetValueOrDefault(type);
            if (used >= retrieval.Quotas.GetValueOrDefault(type, int.MaxValue))
                continue;

            int cost = EstimateTokens(scored.Chunk);
            if (tokens + cost > context.TokenBudget)
                continue;

            tokens += cost;
            perType[type] = used + 1;
            selected.Add(scored);
        }

        return selected;
    }

    /// <summary>Rough token estimate of a chunk as injected (about four characters per token plus formatting).</summary>
    public static int EstimateTokens(MemoryChunk chunk) => (chunk.Title.Length + chunk.Content.Length + 8 + 3) / 4;

    private static ScoredChunk Score(ChunkCandidate candidate, ScoringContext context, RetrievalOptions retrieval)
    {
        MemoryChunk chunk = candidate.Chunk;
        double ageDays = Math.Max(0, (context.Now - chunk.UpdatedAt).TotalDays);
        double halfLife = retrieval.HalfLifeDays.GetValueOrDefault(chunk.Type, 365);
        double freshness = Math.Pow(0.5, ageDays / halfLife);
        double project = chunk.ProjectId switch
        {
            null => 1,
            { } id when context.ActiveProjects.Contains(id) => retrieval.ActiveProjectBoost,
            _ => retrieval.InactiveProjectPenalty,
        };
        return new ScoredChunk(chunk, candidate.Similarity, candidate.Similarity * (chunk.Importance / 5.0) * freshness * project);
    }
}
