namespace Marshal.Abstractions.Mcp;

/// <summary>
/// Current state of one MCP server, for <c>/health</c> and the config view.
/// </summary>
/// <param name="ServerId">The server id.</param>
/// <param name="State">Connection state.</param>
/// <param name="Error">Why the server is down; null otherwise.</param>
/// <param name="ToolCount">Number of tools the server offers while connected.</param>
public sealed record McpServerStatus(string ServerId, McpServerState State, string? Error, int ToolCount)
{
    /// <summary>Problems that leave the server usable, e.g. an <c>untrustedTools</c> glob that matches no tool.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
