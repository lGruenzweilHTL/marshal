using Marshal.Abstractions.Tools;

namespace Marshal.Core.Tools;

/// <summary>
/// Marshal's own tools registered in dependency injection, with the namespace descriptions registered next to them.
/// </summary>
public sealed class BuiltInToolSource(IEnumerable<ITool> tools, IEnumerable<ToolNamespace> namespaces) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = [.. tools];

    public string Name => "built-in";

    public bool IsBuiltIn => true;

    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [.. namespaces.DistinctBy(item => item.Name)];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
