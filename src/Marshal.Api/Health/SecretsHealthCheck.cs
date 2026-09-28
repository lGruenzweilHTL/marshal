using Marshal.Core.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marshal.Api.Health;

/// <summary>
/// Required secrets are checked at startup; unresolved optional references are warnings naming the reference.
/// </summary>
internal sealed class SecretsHealthCheck(SecretHealth secrets) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> missing = secrets.MissingOptional;
        return Task.FromResult(missing.Count == 0
            ? HealthCheckResult.Healthy("resolved")
            : HealthCheckResult.Degraded("optional secrets missing", data: new Dictionary<string, object> { ["warnings"] = missing.Select(reference => $"secret reference {reference} does not resolve").ToArray() }));
    }
}
