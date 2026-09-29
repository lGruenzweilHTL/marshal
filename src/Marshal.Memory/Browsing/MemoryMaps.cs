using System.Data.Common;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Memory.Browsing;

/// <summary>
/// The memory graph and the PCA scatter of the owner's live chunks.
/// </summary>
public sealed class MemoryMaps(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext) : IMemoryMaps
{
    public async Task<MemoryGraph> GetGraphAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<Chunk> live = await db.Chunks.AsNoTracking().Where(chunk => chunk.DeletedAt == null).ToListAsync(cancellationToken);
        HashSet<ChunkId> ids = [.. live.Select(chunk => chunk.Id)];
        List<ChunkEdge> edges = await db.ChunkEdges.AsNoTracking().ToListAsync(cancellationToken);
        return new MemoryGraph(
            [.. live.Select(chunk => new MemoryGraphNode(chunk.Id, chunk.Title, chunk.Type, chunk.ProjectId, chunk.Pinned, chunk.Importance))],
            [.. edges.Where(edge => ids.Contains(edge.FromId) && ids.Contains(edge.ToId)).Select(edge => new MemoryEdge(edge.FromId, edge.ToId, edge.Kind, edge.CreatedAt))]);
    }

    public async Task<IReadOnlyList<MemoryScatterPoint>> GetScatterAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Dictionary<ChunkId, Chunk> live = await db.Chunks.AsNoTracking().Where(chunk => chunk.DeletedAt == null).ToDictionaryAsync(chunk => chunk.Id, cancellationToken);
        if (live.Count == 0 || await MemoryIndexes.ReadInfoAsync(db, cancellationToken) is null)
            return [];

        // Raw SQL bypasses the owner filters: the owner is filtered explicitly.
        var vectors = new List<(Chunk Chunk, float[] Vector)>();
        string owner = MemoryIndexes.OwnerKey(executionContext.Current!.OwnerId);
        await using (DbCommand command = MemoryIndexes.Command(db, $"SELECT chunk_id, embedding FROM {MemoryIndexes.VectorTable} WHERE owner_id = $owner", ("$owner", owner)))
        await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (live.TryGetValue(ChunkId.Parse(reader.GetString(0), null), out Chunk? chunk))
                    vectors.Add((chunk, Vectors.FromBlob((byte[])reader.GetValue(1))));
            }
        }

        vectors.Sort((a, b) => string.CompareOrdinal(a.Chunk.Id.Value, b.Chunk.Id.Value));
        IReadOnlyList<(double X, double Y)> points = PrincipalComponents.Project([.. vectors.Select(entry => entry.Vector)]);
        return [.. vectors.Select((entry, index) => new MemoryScatterPoint(entry.Chunk.Id, entry.Chunk.Title, entry.Chunk.Type, entry.Chunk.ProjectId, points[index].X, points[index].Y))];
    }
}
