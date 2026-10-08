using Marshal.Abstractions.Artifacts;

namespace Marshal.Abstractions.Tools;

/// <summary>
/// Successful tool result.
/// </summary>
/// <param name="Content">Text given to the model.</param>
/// <param name="Reference">Stable reference to the content (message id, path) recorded instead of the content for sensitive tools.</param>
public sealed record ToolOutput(string Content, string? Reference = null)
{
    /// <summary>Artifacts the call shows in the turn (e.g. <c>artifacts.show</c>); each becomes an <c>artifact</c> event.</summary>
    public IReadOnlyList<ArtifactRef>? Artifacts { get; init; }
}
