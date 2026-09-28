namespace Marshal.Abstractions.Tools;

/// <summary>
/// All tools from all accepted sources, with the id ↔ model-facing name mapping.
/// </summary>
public interface IToolRegistry
{
    /// <summary>Accepted tools.</summary>
    IReadOnlyList<RegisteredTool> Tools { get; }

    /// <summary>Sources rejected by the last refresh.</summary>
    IReadOnlyList<ToolSourceError> Errors { get; }

    /// <summary>Resolves a model-facing name; null when unknown.</summary>
    RegisteredTool? FindByModelName(string modelName);

    /// <summary>Resolves an internal id; null when unknown.</summary>
    RegisteredTool? FindById(ToolId id);

    /// <summary>Reloads tools from every source. A failing source is rejected as a unit.</summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
