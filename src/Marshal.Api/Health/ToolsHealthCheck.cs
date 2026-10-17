using Marshal.Abstractions.Mcp;
using Marshal.Core.Tools;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Health;

/// <summary>
/// A <c>tools.core</c> entry that is neither a built-in namespace nor a configured MCP server's namespace is likely a
/// typo: it loads nothing. It is a warning, not a startup failure, because an MCP server file may come later.
/// </summary>
internal sealed class ToolsHealthCheck(IOptions<ToolsOptions> options, IMcpServerCatalog catalog) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        HashSet<string> known = [.. ReservedNamespaces.All, .. catalog.Servers.Select(server => server.Namespace)];
        string[] unknown = [.. options.Value.CoreNamespaces.Where(name => !known.Contains(name)).Select(name => $"tools.core: unknown namespace '{name}'")];
        return Task.FromResult(unknown.Length == 0
            ? HealthCheckResult.Healthy($"{options.Value.CoreNamespaces.Count} core namespaces")
            : HealthCheckResult.Degraded("unknown core namespaces", data: new Dictionary<string, object> { ["warnings"] = unknown }));
    }
}
