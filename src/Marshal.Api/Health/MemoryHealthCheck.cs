using Marshal.Abstractions;
using Marshal.Data;
using Marshal.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marshal.Api.Health;

/// <summary>
/// Unhealthy when a fresh connection has no <c>sqlite-vec</c>: memory vector search would fail.
/// </summary>
internal sealed class MemoryHealthCheck(IDbContextFactory<MarshalDbContext> contexts) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Result<string> version = await SqliteVectorExtension.ProbeAsync(db, cancellationToken);
        return version.IsSuccess
            ? HealthCheckResult.Healthy($"sqlite-vec {version.Value}")
            : HealthCheckResult.Unhealthy(version.Error.Message);
    }
}
