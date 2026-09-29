using Marshal.Abstractions.Memory;

namespace Marshal.Memory;

/// <summary>
/// Memory tuning (<c>memory</c> in <c>marshal.yaml</c>). Defaults are starting points; tune with real use.
/// </summary>
public sealed class MemoryOptions
{
    public RetrievalOptions Retrieval { get; set; } = new();
}

/// <summary>Automatic retrieval and <c>memory.recall</c>.</summary>
public sealed class RetrievalOptions
{
    /// <summary>Seeds taken from each of vector and keyword search.</summary>
    public int SeedCount { get; set; } = 20;

    /// <summary>Token budget of automatically retrieved chunks per turn (pinned chunks are extra).</summary>
    public int TokenBudget { get; set; } = 600;

    /// <summary>Token budget of <c>memory.recall</c>.</summary>
    public int RecallTokenBudget { get; set; } = 2000;

    /// <summary>Candidates scoring below this are dropped.</summary>
    public double MinScore { get; set; } = 0.05;

    /// <summary>Score factor for chunks of active projects.</summary>
    public double ActiveProjectBoost { get; set; } = 1.5;

    /// <summary>Score factor for chunks of inactive projects (never zero: soft scoping).</summary>
    public double InactiveProjectPenalty { get; set; } = 0.5;

    /// <summary>Similarity given to edge-expanded neighbours, as a factor of their seed's similarity.</summary>
    public double NeighbourFactor { get; set; } = 0.7;

    /// <summary>Freshness half-life per type, in days since the last update.</summary>
    public Dictionary<ChunkType, double> HalfLifeDays { get; set; } = new()
    {
        [ChunkType.Event] = 30,
        [ChunkType.Fact] = 365,
        [ChunkType.Preference] = 365,
        [ChunkType.Person] = 730,
        [ChunkType.Procedure] = 730,
        [ChunkType.Device] = 730,
    };

    /// <summary>Maximum chunks per type in one retrieval.</summary>
    public Dictionary<ChunkType, int> Quotas { get; set; } = new()
    {
        [ChunkType.Event] = 5,
        [ChunkType.Fact] = 6,
        [ChunkType.Preference] = 6,
        [ChunkType.Person] = 4,
        [ChunkType.Procedure] = 3,
        [ChunkType.Device] = 4,
    };
}
