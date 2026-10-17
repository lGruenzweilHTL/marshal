namespace Marshal.Abstractions.Tools;

/// <summary>
/// Immutable result of one registry refresh. A run keeps the snapshot taken at its start, so its tool list never
/// changes mid-turn while sources come and go.
/// </summary>
public sealed class ToolSnapshot
{
    private readonly Dictionary<string, RegisteredTool> _byModelName;
    private readonly Dictionary<ToolId, RegisteredTool> _byId;

    public ToolSnapshot(IReadOnlyList<RegisteredTool> tools, IReadOnlyList<ToolSourceError> errors, IReadOnlyList<ToolNamespace>? namespaces = null)
    {
        Tools = tools;
        Errors = errors;
        Namespaces = namespaces ?? [];
        _byModelName = tools.ToDictionary(tool => tool.ModelName, StringComparer.Ordinal);
        _byId = tools.ToDictionary(tool => tool.Id);
    }

    /// <summary>A snapshot without tools (before the first refresh).</summary>
    public static ToolSnapshot Empty { get; } = new([], []);

    /// <summary>Accepted tools.</summary>
    public IReadOnlyList<RegisteredTool> Tools { get; }

    /// <summary>Descriptions of the namespaces of the accepted sources, one per namespace.</summary>
    public IReadOnlyList<ToolNamespace> Namespaces { get; }

    /// <summary>Sources rejected by this refresh.</summary>
    public IReadOnlyList<ToolSourceError> Errors { get; }

    /// <summary>Resolves a model-facing name; null when unknown.</summary>
    public RegisteredTool? FindByModelName(string modelName) => _byModelName.GetValueOrDefault(modelName);

    /// <summary>Resolves an internal id; null when unknown.</summary>
    public RegisteredTool? FindById(ToolId id) => _byId.GetValueOrDefault(id);
}
