namespace Marshal.Abstractions.Memory;

/// <summary>
/// Whole-memory pictures for the memory view: the graph of live chunks and edges, and a 2D projection of their
/// embeddings (first two principal components, computed server-side).
/// </summary>
public interface IMemoryMaps
{
    Task<MemoryGraph> GetGraphAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryScatterPoint>> GetScatterAsync(CancellationToken cancellationToken);
}

/// <summary>Live chunks and the edges between them.</summary>
/// <param name="Nodes">Live chunks.</param>
/// <param name="Edges">Edges whose both ends are live.</param>
public sealed record MemoryGraph(IReadOnlyList<MemoryGraphNode> Nodes, IReadOnlyList<MemoryEdge> Edges);

/// <summary>A chunk in the graph.</summary>
/// <param name="Id">Chunk id.</param>
/// <param name="Title">Title.</param>
/// <param name="Type">Type.</param>
/// <param name="ProjectId">Project, if any.</param>
/// <param name="Pinned">Pinned flag.</param>
/// <param name="Importance">1–5.</param>
public sealed record MemoryGraphNode(ChunkId Id, string Title, ChunkType Type, ProjectId? ProjectId, bool Pinned, int Importance);

/// <summary>A chunk's embedding projected to 2D.</summary>
/// <param name="Id">Chunk id.</param>
/// <param name="Title">Title.</param>
/// <param name="Type">Type.</param>
/// <param name="ProjectId">Project, if any.</param>
/// <param name="X">First principal component.</param>
/// <param name="Y">Second principal component.</param>
public sealed record MemoryScatterPoint(ChunkId Id, string Title, ChunkType Type, ProjectId? ProjectId, double X, double Y);
