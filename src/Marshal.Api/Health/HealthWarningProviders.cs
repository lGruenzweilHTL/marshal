using Marshal.Abstractions;
using Marshal.Abstractions.Health;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Templates;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Marshal.Core.Mcp;
using Marshal.Core.Satellites;
using Marshal.Core.Secrets;
using Marshal.Core.Tools;
using Marshal.Data;
using Marshal.Data.Persistence;
using Marshal.Integrations.HomeAssistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Health;

// The same conditions as the /health checks, as coded warnings with Marshal-authored messages. Error texts of
// external systems stay in the logs, /health and the config view: warnings reach the model as trusted output.

/// <summary><c>database.unreachable</c>.</summary>
internal sealed class DatabaseWarnings(IDbContextFactory<MarshalDbContext> contexts) : IHealthWarningProvider
{
    public async ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken)
            ? []
            : [new HealthCondition("database", "database.unreachable", HealthSeverity.Error,
                new HealthText("Die Datenbank ist nicht erreichbar.", "The database is unreachable."), "/system")];
    }
}

/// <summary><c>memory.vector_search_unavailable</c>.</summary>
internal sealed class MemoryWarnings(IDbContextFactory<MarshalDbContext> contexts) : IHealthWarningProvider
{
    public async ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Result<string> version = await SqliteVectorExtension.ProbeAsync(db, cancellationToken);
        return version.IsSuccess
            ? []
            : [new HealthCondition("memory", "memory.vector_search_unavailable", HealthSeverity.Error,
                new HealthText("Die Vektorsuche des Gedächtnisses ist nicht verfügbar.", "Memory vector search is unavailable."), "/system")];
    }
}

/// <summary><c>config.reload_failed</c>, <c>config.skill_rejected</c>, <c>config.template_rejected</c>.</summary>
internal sealed class ConfigurationWarnings(YamlConfigFile<RiskRulesFile> risk, YamlConfigFile<SatellitesFile> satellites, ISkillCatalog skills, ITemplateCatalog templates) : IHealthWarningProvider
{
    private const string Link = "/system/config";

    public ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<HealthCondition>>(
        [
            .. new[] { (risk.FileName, risk.LastError), (satellites.FileName, satellites.LastError) }
                .Where(file => file.LastError is not null)
                .Select(file => new HealthCondition($"config:{file.FileName}", "config.reload_failed", HealthSeverity.Warning,
                    new HealthText($"{file.FileName} konnte nicht neu geladen werden; die vorherige Version ist aktiv.", $"{file.FileName} failed to reload; the previous version is active."), Link)),
            .. skills.Errors.Select(error => new HealthCondition($"config:skills/{error.File}", "config.skill_rejected", HealthSeverity.Warning,
                new HealthText($"Der Skill skills/{error.File} wurde abgelehnt.", $"The skill skills/{error.File} was rejected."), Link)),
            .. templates.Errors.Select(error => new HealthCondition($"config:templates/{error.File}", "config.template_rejected", HealthSeverity.Warning,
                new HealthText($"Die Vorlage templates/{error.File} wurde abgelehnt.", $"The template templates/{error.File} was rejected."), Link)),
        ]);
}

/// <summary><c>secrets.missing</c>, naming the reference, never a value.</summary>
internal sealed class SecretWarnings(SecretHealth secrets) : IHealthWarningProvider
{
    public ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<HealthCondition>>(
        [
            .. secrets.MissingOptional.Select(reference => new HealthCondition($"secrets:{reference}", "secrets.missing", HealthSeverity.Warning,
                new HealthText($"Die Secret-Referenz {reference} lässt sich nicht auflösen.", $"The secret reference {reference} does not resolve."), "/security/secrets")),
        ]);
}

/// <summary><c>tools.unknown_core_namespace</c>.</summary>
internal sealed class ToolsWarnings(IOptions<ToolsOptions> options, IMcpServerCatalog catalog) : IHealthWarningProvider
{
    public ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken)
    {
        HashSet<string> known = [.. ReservedNamespaces.All, .. catalog.Servers.Select(server => server.Namespace)];
        return ValueTask.FromResult<IReadOnlyList<HealthCondition>>(
        [
            .. options.Value.CoreNamespaces.Where(name => !known.Contains(name)).Select(name => new HealthCondition($"tools:{name}", "tools.unknown_core_namespace", HealthSeverity.Warning,
                new HealthText($"tools.core nennt den unbekannten Namensraum '{name}'.", $"tools.core names the unknown namespace '{name}'."), "/system/config")),
        ]);
    }
}

/// <summary><c>homeassistant.disconnected</c>.</summary>
internal sealed class HomeAssistantWarnings(IHomeAssistantCache cache, IOptions<HomeAssistantOptions> options) : IHealthWarningProvider
{
    public ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<HealthCondition>>(options.Value.Url is null || cache.Current is not null
            ? []
            : [new HealthCondition("homeassistant", "homeassistant.disconnected", HealthSeverity.Warning,
                new HealthText("Home Assistant ist nicht verbunden; Hausteuerung und Timer warten darauf.", "Home Assistant is not connected; home control and timers wait for it."), "/system")]);
}

/// <summary><c>mcp.unreachable</c>, <c>mcp.untrusted_tools_unmatched</c>, <c>mcp.config_rejected</c>.</summary>
internal sealed class McpWarnings(McpToolSources sources, IMcpServerCatalog catalog) : IHealthWarningProvider
{
    private const string Link = "/system/config";

    public ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<HealthCondition>>(
        [
            .. sources.Statuses.Where(status => status.State == McpServerState.Down).Select(status => new HealthCondition($"mcp:{status.ServerId}", "mcp.unreachable", HealthSeverity.Warning,
                new HealthText($"Der MCP-Server {status.ServerId} ist nicht erreichbar.", $"The MCP server {status.ServerId} is unreachable."), Link)),
            .. sources.Statuses.Where(status => status.UnmatchedUntrustedTools.Count > 0).Select(status => new HealthCondition($"mcp:{status.ServerId}", "mcp.untrusted_tools_unmatched", HealthSeverity.Warning,
                new HealthText(
                    $"untrustedTools des MCP-Servers {status.ServerId} trifft kein Werkzeug: {string.Join(", ", status.UnmatchedUntrustedTools)}.",
                    $"untrustedTools of the MCP server {status.ServerId} matches no tool: {string.Join(", ", status.UnmatchedUntrustedTools)}."), Link)),
            .. catalog.Errors.Select(error => new HealthCondition($"mcp:{error.File}", "mcp.config_rejected", HealthSeverity.Warning,
                new HealthText($"Die MCP-Server-Datei mcp/{error.File} wurde abgelehnt.", $"The MCP server file mcp/{error.File} was rejected."), Link)),
        ]);
}
