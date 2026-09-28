namespace Marshal.Core.Mcp;

/// <summary>
/// Thrown when the tools of an MCP server are requested while it is down; the registry rejects the source
/// for that refresh and keeps all others.
/// </summary>
public sealed class McpServerUnavailableException : Exception
{
    public McpServerUnavailableException()
    {
    }

    public McpServerUnavailableException(string message)
        : base(message)
    {
    }

    public McpServerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
