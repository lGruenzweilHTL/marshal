using Marshal.Integrations.HomeAssistant;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Health;

/// <summary>
/// Home Assistant not connected (or still loading) is a warning: home control and timers wait for it.
/// </summary>
internal sealed class HomeAssistantHealthCheck(IHomeAssistantCache cache, IOptions<HomeAssistantOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(options.Value.Url is null ? HealthCheckResult.Healthy("not configured")
            : cache.Current is not null ? HealthCheckResult.Healthy("connected")
            : HealthCheckResult.Degraded("not connected", data: new Dictionary<string, object> { ["warnings"] = new[] { $"Home Assistant is not connected: {cache.LastError ?? "connecting"}" } }));
}
