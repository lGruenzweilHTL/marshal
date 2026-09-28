namespace Marshal.Abstractions.Tools;

/// <summary>
/// All tools from all accepted sources, with the id ↔ model-facing name mapping.
/// </summary>
public interface IToolRegistry
{
    /// <summary>The snapshot of the last refresh.</summary>
    ToolSnapshot Current { get; }

    /// <summary>
    /// Reloads tools from every static and dynamic source and makes the result current. A failing source is
    /// rejected as a unit.
    /// </summary>
    Task<ToolSnapshot> RefreshAsync(CancellationToken cancellationToken);
}
