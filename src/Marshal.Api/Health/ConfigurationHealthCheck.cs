using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Templates;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Marshal.Core.Satellites;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marshal.Api.Health;

/// <summary>
/// Degraded while a hot-reloaded file failed its last reload (the previous version stays active) or a skill is
/// or template is rejected.
/// </summary>
internal sealed class ConfigurationHealthCheck(YamlConfigFile<RiskRulesFile> risk, YamlConfigFile<SatellitesFile> satellites, ISkillCatalog skills, ITemplateCatalog templates) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        string[] failed =
        [
            .. new[] { (risk.FileName, risk.LastError), (satellites.FileName, satellites.LastError) }
                .Where(file => file.LastError is not null)
                .Select(file => $"{file.FileName}: last reload failed, previous version active"),
            .. skills.Errors.Select(error => $"skills/{error.Message}"),
            .. templates.Errors.Select(error => $"templates/{error.Message}"),
        ];
        return Task.FromResult(failed.Length == 0
            ? HealthCheckResult.Healthy("loaded")
            : HealthCheckResult.Degraded("reload failed", data: new Dictionary<string, object> { ["warnings"] = failed }));
    }
}
