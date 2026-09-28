using Marshal.Abstractions.Configuration;

namespace Marshal.Abstractions.Mcp;

/// <summary>
/// The configured MCP servers, hot-reloaded. Invalid servers are rejected individually.
/// </summary>
public interface IMcpServerCatalog
{
    /// <summary>Accepted servers, ordered by id.</summary>
    IReadOnlyList<McpServerDefinition> Servers { get; }

    /// <summary>Files or servers rejected by the last load.</summary>
    IReadOnlyList<ConfigFileError> Errors { get; }

    /// <summary>Calls <paramref name="listener"/> after every reload; dispose to unsubscribe.</summary>
    IDisposable OnChange(Action listener);

    /// <summary>Resolves the server's secret references for one connection.</summary>
    /// <returns>The settings, or a failure naming the unresolved reference.</returns>
    Task<Result<McpConnectionSettings>> ResolveAsync(McpServerDefinition server, CancellationToken cancellationToken);
}
