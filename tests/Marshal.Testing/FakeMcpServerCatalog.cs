using Marshal.Abstractions;
using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Mcp;

namespace Marshal.Testing;

/// <summary>
/// MCP server catalog over a mutable list. Resolving a server yields empty headers and environment unless
/// <see cref="ResolveError"/> is set.
/// </summary>
public sealed class FakeMcpServerCatalog : IMcpServerCatalog
{
    private readonly List<Action> _listeners = [];

    /// <summary>Configured servers; call <see cref="Changed"/> after editing.</summary>
    public List<McpServerDefinition> Definitions { get; } = [];

    /// <summary>Returned by <see cref="ResolveAsync"/> when set.</summary>
    public Error? ResolveError { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<McpServerDefinition> Servers => [.. Definitions];

    /// <inheritdoc />
    public IReadOnlyList<ConfigFileError> Errors { get; set; } = [];

    /// <summary>A stdio server definition.</summary>
    public static McpServerDefinition Server(string id, string? toolPrefix = null, bool untrusted = false, IReadOnlyList<string>? untrustedTools = null) => new()
    {
        Id = id,
        Namespace = toolPrefix ?? id,
        Transport = McpTransportKind.Stdio,
        Command = "fake",
        Untrusted = untrusted,
        UntrustedTools = untrustedTools ?? [],
        File = $"{id}.yaml",
    };

    /// <inheritdoc />
    public IDisposable OnChange(Action listener)
    {
        _listeners.Add(listener);
        return new Subscription(() => _listeners.Remove(listener));
    }

    /// <summary>Notifies listeners.</summary>
    public void Changed()
    {
        foreach (Action listener in _listeners.ToArray())
            listener();
    }

    /// <inheritdoc />
    public Task<Result<McpConnectionSettings>> ResolveAsync(McpServerDefinition server, CancellationToken cancellationToken) =>
        Task.FromResult<Result<McpConnectionSettings>>(ResolveError is { } error
            ? error
            : new McpConnectionSettings(server, new Dictionary<string, string>(), new Dictionary<string, string>()));

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
