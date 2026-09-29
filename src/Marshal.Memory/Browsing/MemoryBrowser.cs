using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Memory.Browsing;

/// <summary>
/// Chunk lists and histories for the memory view. Searches run through the hybrid retriever without marking chunks
/// as retrieved.
/// </summary>
public sealed class MemoryBrowser(IDbContextFactory<MarshalDbContext> contexts, IMemoryRetriever retriever) : IMemoryBrowser
{
    // Browsing ranks everything that matches; the prompt budget does not apply.
    private const int SearchTokenBudget = 1_000_000;

    public async Task<IReadOnlyList<MemoryChunk>> ListAsync(MemoryListFilter filter, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            IReadOnlySet<ProjectId> active = filter.Project is { } project ? new HashSet<ProjectId> { project } : new HashSet<ProjectId>();
            IReadOnlyList<RetrievedChunk> found = await retriever.RetrieveAsync(
                new MemoryQuery(filter.Query, null, active, SearchTokenBudget, filter.Type, filter.Project, IncludePinned: true, MarkRetrieved: false),
                cancellationToken);
            return [.. found.Select(chunk => chunk.Chunk)
                .Where(chunk => (filter.Scope is null || chunk.Scope == filter.Scope) && (filter.Pinned is null || chunk.Pinned == filter.Pinned))
                .Take(filter.Limit)];
        }

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<Chunk> query = db.Chunks.AsNoTracking();
        if (!filter.IncludeDeleted)
            query = query.Where(chunk => chunk.DeletedAt == null);
        if (filter.Type is { } type)
            query = query.Where(chunk => chunk.Type == type);
        if (filter.Scope is { } scope)
            query = query.Where(chunk => chunk.Scope == scope);
        if (filter.Project is { } projectId)
            query = query.Where(chunk => chunk.ProjectId == projectId);
        if (filter.Pinned is { } pinned)
            query = query.Where(chunk => chunk.Pinned == pinned);

        // Timestamps are sorted in memory: SQLite cannot order by DateTimeOffset.
        List<Chunk> chunks = await query.ToListAsync(cancellationToken);
        return [.. chunks.OrderByDescending(chunk => chunk.UpdatedAt).Take(filter.Limit).Select(SqliteMemoryStore.ToView)];
    }

    public async Task<IReadOnlyList<MemoryHistoryEntry>?> GetHistoryAsync(ChunkId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Chunk? current = await db.Chunks.AsNoTracking().SingleOrDefaultAsync(chunk => chunk.Id == id, cancellationToken);
        if (current is null)
            return null;

        // Predecessors: follow supersedes edges (new -> old) back from the chunk.
        var chain = new List<Chunk> { current };
        var seen = new HashSet<ChunkId> { id };
        var pending = new Queue<ChunkId>([id]);
        while (pending.TryDequeue(out ChunkId next))
        {
            List<ChunkId> older = await db.ChunkEdges.AsNoTracking()
                .Where(edge => edge.FromId == next && edge.Kind == ChunkEdgeKind.Supersedes)
                .Select(edge => edge.ToId)
                .ToListAsync(cancellationToken);
            foreach (ChunkId predecessor in older.Where(seen.Add))
            {
                chain.Add(await db.Chunks.AsNoTracking().SingleAsync(chunk => chunk.Id == predecessor, cancellationToken));
                pending.Enqueue(predecessor);
            }
        }

        List<ChunkId> ids = [.. chain.Select(chunk => chunk.Id)];
        List<ChunkVersion> versions = await db.ChunkVersions.AsNoTracking().Where(version => ids.Contains(version.ChunkId)).ToListAsync(cancellationToken);
        var entries = new List<MemoryHistoryEntry>();
        foreach (Chunk chunk in chain.OrderBy(chunk => chunk.CreatedAt))
        {
            DateTimeOffset validFrom = chunk.CreatedAt;
            foreach (ChunkVersion version in versions.Where(version => version.ChunkId == chunk.Id).OrderBy(version => version.ReplacedAt))
            {
                entries.Add(new MemoryHistoryEntry(chunk.Id, MemoryHistoryKind.Version, chunk.Title, version.Content, version.Importance, validFrom));
                validFrom = version.ReplacedAt;
            }

            MemoryHistoryKind kind = chunk.Id == id ? MemoryHistoryKind.Current : MemoryHistoryKind.Predecessor;
            entries.Add(new MemoryHistoryEntry(chunk.Id, kind, chunk.Title, chunk.Content, chunk.Importance, validFrom));
        }

        return entries;
    }
}
