namespace Marshal.Abstractions.Mcp;

/// <summary>How Marshal talks to an MCP server.</summary>
public enum McpTransportKind
{
    /// <summary>Child process speaking MCP over stdin / stdout.</summary>
    Stdio,

    /// <summary>Streamable HTTP endpoint.</summary>
    Http,
}
