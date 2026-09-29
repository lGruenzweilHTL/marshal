using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Consolidation;

/// <summary>
/// Computes the memory audit over the owner's chunks and edges. The corpus is small (personal memory), so it is read
/// once and evaluated in memory.
/// </summary>
public sealed class MemoryAudit(IDbContextFactory<MarshalDbContext> contexts, IOptions<MemoryOptions> options, TimeProvider timeProvider) : IMemoryAudit
{
    public async Task<MemoryAuditReport> RunAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<Chunk> chunks = await db.Chunks.AsNoTracking().ToListAsync(cancellationToken);
        List<ChunkEdge> edges = await db.ChunkEdges.AsNoTracking().ToListAsync(cancellationToken);
        HashSet<ProjectId> deletedProjects = [.. await db.Projects.AsNoTracking()
            .Where(project => project.DeletedAt != null)
            .Select(project => project.Id)
            .ToListAsync(cancellationToken)];

        var live = chunks.Where(chunk => chunk.DeletedAt is null).ToDictionary(chunk => chunk.Id);
        DateTimeOffset staleBefore = timeProvider.GetUtcNow().AddDays(-options.Value.Audit.StaleAfterDays);
        List<MemoryChunk> stale = [.. live.Values
            .Where(chunk => !chunk.Pinned && LastActivity(chunk) < staleBefore)
            .OrderBy(LastActivity)
            .Select(SqliteMemoryStore.ToView)];

        HashSet<(ChunkId, ChunkId)> superseded = [.. edges
            .Where(edge => edge.Kind == ChunkEdgeKind.Supersedes)
            .SelectMany(edge => new[] { (edge.FromId, edge.ToId), (edge.ToId, edge.FromId) })];
        List<MemoryContradiction> contradictions = [.. edges
            .Where(edge => edge.Kind == ChunkEdgeKind.Contradicts && !superseded.Contains((edge.FromId, edge.ToId)))
            .Where(edge => live.ContainsKey(edge.FromId) && live.ContainsKey(edge.ToId))
            .OrderBy(edge => edge.CreatedAt)
            .Select(edge => new MemoryContradiction(SqliteMemoryStore.ToView(live[edge.FromId]), SqliteMemoryStore.ToView(live[edge.ToId]), edge.CreatedAt))];

        List<MemoryChunk> orphans = [.. live.Values
            .Where(chunk => chunk.ProjectId is { } project && deletedProjects.Contains(project))
            .OrderBy(chunk => chunk.Title, StringComparer.Ordinal)
            .Select(SqliteMemoryStore.ToView)];

        // A supersedes edge to a deleted chunk is history (the replaced version), not a dangling reference.
        List<MemoryEdge> dangling = [.. edges
            .Where(edge => !live.ContainsKey(edge.FromId) || (!live.ContainsKey(edge.ToId) && edge.Kind != ChunkEdgeKind.Supersedes))
            .OrderBy(edge => edge.CreatedAt)
            .Select(edge => new MemoryEdge(edge.FromId, edge.ToId, edge.Kind, edge.CreatedAt))];

        return new MemoryAuditReport(stale, contradictions, orphans, dangling);
    }

    private static DateTimeOffset LastActivity(Chunk chunk) =>
        chunk.LastRetrievedAt is { } retrieved && retrieved > chunk.UpdatedAt ? retrieved : chunk.UpdatedAt;
}
