using Marshal.Abstractions.Tools;

namespace Marshal.Core.Tools;

/// <summary>
/// Marshal's own tools registered in dependency injection.
/// </summary>
public sealed class BuiltInToolSource(IEnumerable<ITool> tools) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = [.. tools];

    public string Name => "built-in";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
