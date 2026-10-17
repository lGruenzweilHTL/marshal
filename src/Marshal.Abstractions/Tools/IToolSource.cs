namespace Marshal.Abstractions.Tools;

/// <summary>
/// Provides a set of tools: built-in tools now, MCP servers and skills later.
/// </summary>
public interface IToolSource
{
    /// <summary>Name used in error messages (e.g. <c>built-in</c>, an MCP server id).</summary>
    string Name { get; }

    /// <summary>True for Marshal's own tools, which may use reserved namespaces.</summary>
    bool IsBuiltIn { get; }

    /// <summary>Description of every namespace this source's tools use.</summary>
    IReadOnlyList<ToolNamespace> Namespaces { get; }

    /// <summary>The tools currently offered by this source.</summary>
    Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken);
}
