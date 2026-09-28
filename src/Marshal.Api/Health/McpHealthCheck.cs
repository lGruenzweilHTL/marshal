using Marshal.Abstractions.Mcp;
using Marshal.Core.Mcp;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marshal.Api.Health;

/// <summary>
/// MCP servers that are down or still connecting, and rejected server files, are warnings; Marshal keeps working
/// without them.
/// </summary>
internal sealed class McpHealthCheck(McpToolSources sources, IMcpServerCatalog catalog) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<McpServerStatus> statuses = sources.Statuses;
        List<string> warnings =
        [
            .. statuses.Where(status => status.State != McpServerState.Connected).Select(Describe),
            .. catalog.Errors.Select(error => error.Message.StartsWith(error.File, StringComparison.Ordinal) ? $"mcp/{error.Message}" : $"mcp/{error.File}: {error.Message}"),
        ];
        int connected = statuses.Count(status => status.State == McpServerState.Connected);
        return Task.FromResult(warnings.Count == 0
            ? HealthCheckResult.Healthy($"{connected} of {statuses.Count} servers connected")
            : HealthCheckResult.Degraded($"{connected} of {statuses.Count} servers connected", data: new Dictionary<string, object> { ["warnings"] = warnings.ToArray() }));
    }

    private static string Describe(McpServerStatus status) => status.State == McpServerState.Down
        ? $"MCP server {status.ServerId} is down: {status.Error}"
        : $"MCP server {status.ServerId} is connecting";
}
