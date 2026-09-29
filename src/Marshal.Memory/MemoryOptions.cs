using Marshal.Abstractions.Memory;

namespace Marshal.Memory;

/// <summary>
/// Memory tuning (<c>memory</c> in <c>marshal.yaml</c>). Defaults are starting points; tune with real use.
/// </summary>
public sealed class MemoryOptions
{
    public RetrievalOptions Retrieval { get; set; } = new();

    public ProjectActivationOptions Projects { get; set; } = new();

    public SummarizerOptions Summarizer { get; set; } = new();
}

/// <summary>Session-close summarization.</summary>
public sealed class SummarizerOptions
{
    /// <summary>Summarize sessions when they close.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Most recent turns of a session the summarizer reads.</summary>
    public int MaxTurns { get; set; } = 50;

    /// <summary>Token budget of the existing memory shown to the summarizer (to reuse titles and spot contradictions).</summary>
    public int MemoryTokenBudget { get; set; } = 1500;
}

/// <summary>Project activation.</summary>
public sealed class ProjectActivationOptions
{
    /// <summary>Centroid score (after penalties and the recency prior) that activates stickily.</summary>
    public double HighConfidence { get; set; } = 0.75;

    /// <summary>Centroid score that activates for this turn only.</summary>
    public double MediumConfidence { get; set; } = 0.6;

    /// <summary>Weight of the similarity to a rejected phrase subtracted from the centroid score.</summary>
    public double NegativeWeight { get; set; } = 0.5;

    /// <summary>Score added for a project active just now; halves every <see cref="RecencyHalfLifeDays"/>.</summary>
    public double RecencyBoost { get; set; } = 0.05;

    public double RecencyHalfLifeDays { get; set; } = 7;

    /// <summary>Factor applied to a sticky activation's weight on every turn.</summary>
    public double StickyDecay { get; set; } = 0.7;

    /// <summary>Sticky activations below this weight are dropped.</summary>
    public double StickyMinimum { get; set; } = 0.3;

    /// <summary>Consecutive turns less similar than this are a topic shift and drop sticky activations.</summary>
    public double TopicShiftThreshold { get; set; } = 0.3;

    /// <summary>Confirmations that promote an alias candidate to an active alias.</summary>
    public int AliasPromotion { get; set; } = 3;

    /// <summary>Most important project chunks averaged into the centroid with the description.</summary>
    public int CentroidTopChunks { get; set; } = 10;
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
