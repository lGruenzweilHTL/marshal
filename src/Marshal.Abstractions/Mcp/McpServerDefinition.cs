using Marshal.Abstractions.Secrets;

namespace Marshal.Abstractions.Mcp;

/// <summary>
/// A configured MCP server (<c>/config/mcp/*.yaml</c>). Holds secret names only; values are resolved per connection.
/// </summary>
public sealed record McpServerDefinition
{
    /// <summary>Server id (<c>[a-z][a-z0-9]*</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Tool namespace: <c>toolPrefix</c> when set, otherwise the id.</summary>
    public required string Namespace { get; init; }

    /// <summary>Transport.</summary>
    public required McpTransportKind Transport { get; init; }

    /// <summary>Streamable HTTP endpoint (http transport).</summary>
    public Uri? Url { get; init; }

    /// <summary>Executable (stdio transport).</summary>
    public string? Command { get; init; }

    /// <summary>Command-line arguments (stdio transport).</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Secret sent as <c>Authorization: Bearer</c> (http) or ignored (stdio).</summary>
    public SecretName? AuthTokenSecret { get; init; }

    /// <summary>Environment variables of the child process taken from secrets (stdio).</summary>
    public IReadOnlyDictionary<string, SecretName> EnvironmentSecrets { get; init; } = new Dictionary<string, SecretName>();

    /// <summary>Tool output is third-party content: results taint the run with source <c>mcp:&lt;id&gt;</c>.</summary>
    public bool Untrusted { get; init; }

    /// <summary>
    /// Globs on the tool names the server reports; matching tools taint the run like an untrusted server's (e.g. a
    /// to-do read tool of an otherwise trusted server). Empty with <see cref="Untrusted"/>.
    /// </summary>
    public IReadOnlyList<string> UntrustedTools { get; init; } = [];

    /// <summary>File the server was loaded from.</summary>
    public required string File { get; init; }
}
