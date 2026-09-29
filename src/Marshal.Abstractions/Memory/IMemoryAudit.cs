namespace Marshal.Abstractions.Memory;

/// <summary>
/// Health of the current owner's memory: stale chunks, open contradictions and orphans.
/// </summary>
public interface IMemoryAudit
{
    Task<MemoryAuditReport> RunAsync(CancellationToken cancellationToken);
}

/// <summary>An audit result.</summary>
/// <param name="Stale">Live, unpinned chunks neither updated nor retrieved for the configured period, oldest first.</param>
/// <param name="Contradictions">Live chunk pairs linked by <c>contradicts</c> where neither supersedes the other.</param>
/// <param name="Orphans">Live chunks of deleted projects.</param>
/// <param name="DanglingEdges">Edges touching a deleted chunk (except <c>supersedes</c> edges to the replaced chunk, which are history).</param>
public sealed record MemoryAuditReport(
    IReadOnlyList<MemoryChunk> Stale,
    IReadOnlyList<MemoryContradiction> Contradictions,
    IReadOnlyList<MemoryChunk> Orphans,
    IReadOnlyList<MemoryEdge> DanglingEdges);

/// <summary>An open contradiction.</summary>
/// <param name="From">The contradicting chunk (edge source).</param>
/// <param name="To">The contradicted chunk.</param>
/// <param name="Since">When the edge was added.</param>
public sealed record MemoryContradiction(MemoryChunk From, MemoryChunk To, DateTimeOffset Since);
