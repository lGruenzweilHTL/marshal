using System.Data.Common;
using System.Text.RegularExpressions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Retrieval;

/// <summary>
/// Two-phase hybrid retrieval: vector (sqlite-vec KNN) and keyword (FTS5 bm25) seeds, then one breadth-first step
/// along <c>relates_to</c> / <c>depends_on</c> edges; superseded chunks are replaced by their live successors, and
/// <see cref="IChunkScorer"/> picks the result. Returned chunks get their <c>last_retrieved_at</c> set.
/// </summary>
public sealed partial class HybridMemoryRetriever(
    IDbContextFactory<MarshalDbContext> contexts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IExecutionContextAccessor executionContext,
    IChunkScorer scorer,
    IOptions<MemoryOptions> options,
    TimeProvider timeProvider) : IMemoryRetriever
{
    // Keyword hits rank just below an exact vector match, relative to the best bm25 score of the query.
    private const double KeywordWeight = 0.9;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(MemoryQuery query, CancellationToken cancellationToken)
    {
        RetrievalOptions retrieval = options.Value.Retrieval;
        ReadOnlyMemory<float> embedding = query.Embedding
            ?? (await embeddings.GenerateAsync([query.Text], cancellationToken: cancellationToken)).Single().Vector;
        string owner = MemoryIndexes.OwnerKey(Owner);

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = new Dictionary<ChunkId, (double Similarity, RetrievalSource Source)>();
        foreach ((ChunkId id, double similarity) in await VectorSeedsAsync(db, owner, embedding, retrieval.SeedCount, cancellationToken))
            Add(found, id, similarity, RetrievalSource.Vector);
        foreach ((ChunkId id, double similarity) in await KeywordSeedsAsync(db, owner, query.Text, retrieval.SeedCount, KeywordIndex.Chunks, cancellationToken))
            Add(found, id, similarity, RetrievalSource.Keyword);
        if (query.AsOf is not null)
        {
            foreach ((ChunkId id, double similarity) in await KeywordSeedsAsync(db, owner, query.Text, retrieval.SeedCount, KeywordIndex.Versions, cancellationToken))
                Add(found, id, similarity, RetrievalSource.Keyword);
        }

        List<ChunkId> seeds = [.. found.Keys];
        var seedSimilarity = found.ToDictionary(entry => entry.Key, entry => entry.Value.Similarity);
        List<ChunkEdge> edges = await db.ChunkEdges.AsNoTracking()
            .Where(edge => seeds.Contains(edge.FromId) || seeds.Contains(edge.ToId))
            .ToListAsync(cancellationToken);
        foreach (ChunkEdge edge in edges.Where(edge => edge.Kind is ChunkEdgeKind.RelatesTo or ChunkEdgeKind.DependsOn))
        {
            // From seeds only; vector search returns up to k chunks, many with near-zero similarity.
            foreach ((ChunkId seed, ChunkId neighbour) in new[] { (edge.FromId, edge.ToId), (edge.ToId, edge.FromId) })
            {
                if (seedSimilarity.TryGetValue(seed, out double similarity))
                    Add(found, neighbour, similarity * retrieval.NeighbourFactor, RetrievalSource.Edge);
            }
        }

        Dictionary<ChunkId, (double Similarity, RetrievalSource Source)> replaced = await ReplaceSupersededAsync(db, found, cancellationToken);
        List<ChunkId> ids = [.. replaced.Keys];
        List<Chunk> chunks = await db.Chunks.AsNoTracking()
            .Where(chunk => ids.Contains(chunk.Id) && chunk.DeletedAt == null)
            .ToListAsync(cancellationToken);
        List<ChunkCandidate> candidates =
        [
            .. chunks
                .Where(chunk => query.IncludePinned || !chunk.Pinned)
                .Where(chunk => query.Type is null || chunk.Type == query.Type)
                .Where(chunk => query.Project is null || chunk.ProjectId == query.Project)
                .Select(chunk => new ChunkCandidate(SqliteMemoryStore.ToView(chunk), replaced[chunk.Id].Similarity)),
        ];

        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<ScoredChunk> selected = scorer.Select(candidates, new ScoringContext(query.ActiveProjects, now, query.TokenBudget));
        List<ChunkId> selectedIds = [.. selected.Select(chunk => chunk.Chunk.Id)];
        if (query.MarkRetrieved && selectedIds.Count > 0)
        {
            await db.Chunks.Where(chunk => selectedIds.Contains(chunk.Id))
                .ExecuteUpdateAsync(update => update.SetProperty(chunk => chunk.LastRetrievedAt, now), cancellationToken);
        }

        return [.. selected.Select(chunk => new RetrievedChunk(query.MarkRetrieved ? chunk.Chunk with { LastRetrievedAt = now } : chunk.Chunk, chunk.Similarity, chunk.Score, replaced[chunk.Chunk.Id].Source))];
    }

    public async Task<IReadOnlyList<MemoryChunk>> GetPinnedAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<Chunk> pinned = await db.Chunks.AsNoTracking()
            .Where(chunk => chunk.Pinned && chunk.DeletedAt == null)
            .ToListAsync(cancellationToken);
        return [.. pinned.OrderByDescending(chunk => chunk.Importance).ThenBy(chunk => chunk.Title, StringComparer.Ordinal).Select(SqliteMemoryStore.ToView)];
    }

    private OwnerId Owner => executionContext.Current?.OwnerId
        ?? throw new InvalidOperationException("Memory accessed without an execution context.");

    private static void Add(Dictionary<ChunkId, (double Similarity, RetrievalSource Source)> found, ChunkId id, double similarity, RetrievalSource source)
    {
        if (!found.TryGetValue(id, out (double Similarity, RetrievalSource Source) existing) || existing.Similarity < similarity)
            found[id] = (similarity, source);
    }

    private static async Task<List<(ChunkId Id, double Similarity)>> VectorSeedsAsync(
        MarshalDbContext db, string owner, ReadOnlyMemory<float> embedding, int count, CancellationToken cancellationToken)
    {
        var seeds = new List<(ChunkId, double)>();
        if (await MemoryIndexes.ReadInfoAsync(db, cancellationToken) is not { } info || info.Dimensions != embedding.Length)
            return seeds;

        await using DbCommand command = MemoryIndexes.Command(db,
            $"SELECT chunk_id, distance FROM {MemoryIndexes.VectorTable} WHERE embedding MATCH $embedding AND k = $k AND owner_id = $owner",
            ("$embedding", MemoryIndexes.ToBlob(embedding.Span)), ("$k", count), ("$owner", owner));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            seeds.Add((ChunkId.Parse(reader.GetString(0), null), Math.Clamp(1 - reader.GetDouble(1), 0, 1)));

        return seeds;
    }

    private static async Task<List<(ChunkId Id, double Similarity)>> KeywordSeedsAsync(
        MarshalDbContext db, string owner, string text, int count, CancellationToken cancellationToken)
    {
        var seeds = new List<(ChunkId, double)>();
        string[] terms = [.. Words().Matches(text).Select(match => match.Value).Where(word => word.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (terms.Length == 0)
            return seeds;

        // Every term is quoted, so FTS5 query syntax in user text is taken literally.
        string match = string.Join(" OR ", terms.Select(term => $"\"{term}\""));
        await using DbCommand command = MemoryIndexes.Command(db,
            "SELECT chunk_id, bm25(chunks_fts) AS rank FROM chunks_fts WHERE chunks_fts MATCH $match AND owner_id = $owner ORDER BY rank LIMIT $k",
            ("$match", match), ("$owner", owner), ("$k", count));
        var ranked = new List<(ChunkId Id, double Rank)>();
        await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                ranked.Add((ChunkId.Parse(reader.GetString(0), null), reader.GetDouble(1)));
        }

        // bm25 is negative, lower is better; the best hit gets the full keyword weight.
        double best = ranked.Count > 0 ? ranked.Min(hit => hit.Rank) : 0;
        foreach ((ChunkId id, double rank) in ranked)
            seeds.Add((id, best < 0 ? KeywordWeight * rank / best : KeywordWeight));

        return seeds;
    }

    // Follows supersedes edges (new → old) from each candidate to its newest live successor.
    private static async Task<Dictionary<ChunkId, (double Similarity, RetrievalSource Source)>> ReplaceSupersededAsync(
        MarshalDbContext db, Dictionary<ChunkId, (double Similarity, RetrievalSource Source)> found, CancellationToken cancellationToken)
    {
        var result = new Dictionary<ChunkId, (double Similarity, RetrievalSource Source)>();
        foreach ((ChunkId id, (double similarity, RetrievalSource source)) in found)
        {
            ChunkId current = id;
            RetrievalSource via = source;
            var seen = new HashSet<ChunkId> { current };
            while (await db.ChunkEdges.AsNoTracking()
                       .Where(edge => edge.ToId == current && edge.Kind == ChunkEdgeKind.Supersedes)
                       .Join(db.Chunks.Where(chunk => chunk.DeletedAt == null), edge => edge.FromId, chunk => chunk.Id, (edge, chunk) => chunk.Id)
                       .FirstOrDefaultAsync(cancellationToken) is { Value: not null } successor
                   && seen.Add(successor))
            {
                current = successor;
                via = RetrievalSource.Successor;
            }

            Add(result, current, similarity, via);
        }

        return result;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
}
