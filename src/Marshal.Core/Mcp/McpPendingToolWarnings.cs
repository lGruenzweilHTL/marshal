using Marshal.Abstractions.Health;
using Marshal.Abstractions.Mcp;

namespace Marshal.Core.Mcp;

/// <summary>
/// <c>mcp.tool_pending</c> per server with tools waiting for approval. Counts only: tool names come from the server.
/// </summary>
public sealed class McpPendingToolWarnings(IMcpToolPins pins) : IHealthWarningProvider
{
    public async ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken) =>
    [
        .. (await pins.ListPendingAsync(cancellationToken))
            .GroupBy(pin => pin.ServerId, StringComparer.Ordinal)
            .Select(server => new HealthCondition($"mcp:{server.Key}", "mcp.tool_pending", HealthSeverity.Warning, Message(server.Key, server.Count()), "/system/config")),
    ];

    private static HealthText Message(string serverId, int count) => count == 1
        ? new HealthText($"Ein Werkzeug des MCP-Servers {serverId} wartet auf Freigabe.", $"A tool of the MCP server {serverId} waits for approval.")
        : new HealthText($"{count} Werkzeuge des MCP-Servers {serverId} warten auf Freigabe.", $"{count} tools of the MCP server {serverId} wait for approval.");
}
