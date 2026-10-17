using Marshal.Abstractions.Tools;

namespace Marshal.Testing;

/// <summary>
/// Tool source over a fixed list of tools.
/// </summary>
public sealed class FakeToolSource(string name, bool isBuiltIn, params ITool[] tools) : IToolSource
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public bool IsBuiltIn => isBuiltIn;

    /// <summary>Descriptions to declare; when empty, each tool namespace gets <c>Test tools of &lt;namespace&gt;</c>.</summary>
    public List<ToolNamespace> Descriptions { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<ToolNamespace> Namespaces => Descriptions.Count > 0
        ? [.. Descriptions]
        : [.. Tools.Select(tool => tool.Descriptor.Id.Namespace).Distinct().Select(name => new ToolNamespace(name, $"Test tools of {name}"))];

    /// <summary>Tools offered; may be changed between registry refreshes.</summary>
    public List<ITool> Tools { get; } = [.. tools];

    /// <inheritdoc />
    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ITool>>([.. Tools]);
}
