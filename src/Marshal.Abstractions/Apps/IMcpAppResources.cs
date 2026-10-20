namespace Marshal.Abstractions.Apps;

/// <summary>MCP app resources of the configured servers, for rendering a stored app again.</summary>
public interface IMcpAppResources
{
    /// <summary>
    /// The server's tool namespace when the server is configured with <c>apps: true</c>; null otherwise (removed,
    /// apps turned off).
    /// </summary>
    string? AppNamespace(string server);

    /// <summary>Reads a <c>ui://</c> resource of a server with <c>apps: true</c> (cached like for tool calls).</summary>
    Task<Result<string>> ReadAsync(string server, string uri, CancellationToken cancellationToken);
}
