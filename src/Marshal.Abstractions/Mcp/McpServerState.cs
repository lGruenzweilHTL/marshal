namespace Marshal.Abstractions.Mcp;

/// <summary>
/// Connection state of a configured MCP server.
/// </summary>
public enum McpServerState
{
    /// <summary>The first connection attempt is running.</summary>
    Connecting,

    /// <summary>Connected; its tools are offered.</summary>
    Connected,

    /// <summary>Unreachable, failed discovery or disconnected; its tools are hidden while it reconnects.</summary>
    Down,
}
