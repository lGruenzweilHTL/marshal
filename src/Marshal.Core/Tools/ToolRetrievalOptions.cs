using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.Tools;

/// <summary>
/// Tool retrieval by namespace (<c>tools.retrieval</c>).
/// </summary>
public sealed class ToolRetrievalOptions
{
    /// <summary>False offers every visible tool on every call.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum cosine similarity between the turn and a namespace description.</summary>
    [Range(0.0, 1.0, ErrorMessage = "tools.retrieval.threshold must be between {1} and {2}.")]
    public double Threshold { get; set; } = 0.30;

    /// <summary>Most namespaces loaded by retrieval (or one <c>agent.find_tools</c> call) per turn.</summary>
    [Range(1, 20, ErrorMessage = "tools.retrieval.maxPerTurn must be between {1} and {2}.")]
    public int MaxPerTurn { get; set; } = 3;

    /// <summary>Retrieval is skipped when no more tools than this are visible.</summary>
    [Range(0, 1000, ErrorMessage = "tools.retrieval.minTools must be between {1} and {2}.")]
    public int MinTools { get; set; } = 24;
}
