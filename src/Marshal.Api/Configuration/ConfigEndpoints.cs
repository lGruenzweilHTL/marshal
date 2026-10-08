using Marshal.Abstractions;
using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Api.Auth;
using Marshal.Core;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Marshal.Core.Mcp;
using Marshal.Core.Models;
using Marshal.Core.Satellites;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Configuration;

/// <summary>
/// <c>/config</c>: read-only summary of the loaded configuration for admins. Secret names only, never values;
/// nothing here edits files.
/// </summary>
internal static class ConfigEndpoints
{
    public static RouteGroupBuilder MapConfigEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/config", GetSummary).RequireAuthorization(Policies.Admin).WithTags("Config").WithName("GetConfigSummary")
            .WithSummary("Loaded skills, templates, MCP servers, risk rules, models and satellites, plus the last load error per file.");
        return group;
    }

    private static Ok<ConfigSummary> GetSummary(
        YamlConfigFile<RiskRulesFile> risk,
        YamlConfigFile<SatellitesFile> satellites,
        ISkillCatalog skills,
        ITemplateCatalog templates,
        IMcpServerCatalog servers,
        McpToolSources mcp,
        IOptions<ModelRoutingOptions> models)
    {
        // Hot-reloaded single files keep their previous version on a failed reload; directory entries are skipped.
        List<ConfigError> errors =
        [
            .. new[] { (risk.FileName, risk.LastError), (satellites.FileName, satellites.LastError) }.Where(file => file.LastError is not null)
                .Select(file => new ConfigError(file.FileName, file.LastError!, PreviousVersionActive: true)),
            .. Prefixed(CoreServiceCollectionExtensions.McpDirectory, servers.Errors),
            .. Prefixed(CoreServiceCollectionExtensions.SkillsDirectory, skills.Errors),
            .. Prefixed(CoreServiceCollectionExtensions.TemplatesDirectory, templates.Errors),
        ];

        var statuses = mcp.Statuses.ToDictionary(status => status.ServerId, StringComparer.Ordinal);
        Result<IReadOnlyList<RiskRule>> rules = RiskRulesFile.Compile(risk.CurrentValue);

        return TypedResults.Ok(new ConfigSummary(
            errors,
            [.. skills.Skills.Select(skill => new SkillSummary(skill.Name, skill.Description, [.. skill.Files.Keys.Order(StringComparer.Ordinal)]))],
            [.. templates.Templates.Select(template => new TemplateSummary(
                template.Name, template.Description, template.ModelTier, template.Tools, template.Skills, template.MaxSteps, template.MaxAdvisorCalls,
                BuiltIn: ReferenceEquals(template, AgentTemplate.BuiltInDefault)))],
            [.. servers.Servers.Select(server => Server(server, statuses.GetValueOrDefault(server.Id)))],
            rules.IsSuccess ? [.. rules.Value.Select(rule => new RiskRuleSummary(rule.ToolPattern, rule.TargetConditions, rule.ArgumentConditions, rule.Tier))] : [],
            [.. models.Value.Models.OrderBy(model => model.Key, StringComparer.Ordinal).Select(model => new ModelSummary(
                model.Key, model.Value.Tier, model.Value.Provider, model.Value.Model, model.Value.Endpoint?.ToString(), model.Value.ApiKeySecret,
                model.Value.Vision, model.Value.Tools, model.Value.ContextLength))],
            [.. satellites.CurrentValue.Satellites.OrderBy(satellite => satellite.Key, StringComparer.Ordinal).Select(satellite => new SatelliteSummary(
                satellite.Key, satellite.Value.Verbosity, satellite.Value.Notes, satellite.Value.DenyTools, satellite.Value.DenyConfirm))]));
    }

    private static IEnumerable<ConfigError> Prefixed(string directory, IReadOnlyList<ConfigFileError> errors) =>
        errors.Select(error => new ConfigError($"{directory}/{error.File}", error.Message, PreviousVersionActive: false));

    private static McpServerSummary Server(McpServerDefinition server, McpServerStatus? status) => new(
        server.Id,
        server.Namespace,
        server.Transport == McpTransportKind.Http ? "http" : "stdio",
        server.Transport == McpTransportKind.Http ? server.Url?.ToString() ?? "" : string.Join(' ', [server.Command, .. server.Arguments]),
        server.Untrusted,
        server.AuthTokenSecret?.Value,
        [.. server.EnvironmentSecrets.Values.Select(secret => secret.Value).Order(StringComparer.Ordinal)],
        status?.State ?? McpServerState.Connecting,
        status?.Error,
        status?.ToolCount ?? 0,
        server.File,
        server.UntrustedTools,
        status?.Warnings ?? []);
}

/// <summary>Read-only summary of the loaded <c>/config</c>.</summary>
/// <param name="Errors">Files whose last load failed.</param>
/// <param name="Skills">Loaded skills.</param>
/// <param name="Templates">Loaded agent templates, including the built-in default unless replaced.</param>
/// <param name="McpServers">Configured MCP servers with their connection status.</param>
/// <param name="RiskRules">Active risk rules in evaluation order (first match wins).</param>
/// <param name="Models">Configured models; API keys by secret name only.</param>
/// <param name="Satellites">Satellite profiles.</param>
public sealed record ConfigSummary(
    IReadOnlyList<ConfigError> Errors,
    IReadOnlyList<SkillSummary> Skills,
    IReadOnlyList<TemplateSummary> Templates,
    IReadOnlyList<McpServerSummary> McpServers,
    IReadOnlyList<RiskRuleSummary> RiskRules,
    IReadOnlyList<ModelSummary> Models,
    IReadOnlyList<SatelliteSummary> Satellites);

/// <summary>A config file whose last load failed.</summary>
/// <param name="File">Path relative to <c>/config</c>.</param>
/// <param name="Message">What is wrong (never contains secret values).</param>
/// <param name="PreviousVersionActive">True when the previous version of the file stays active; false when the entry is skipped.</param>
public sealed record ConfigError(string File, string Message, bool PreviousVersionActive);

/// <summary>A loaded skill.</summary>
/// <param name="Name">Skill name.</param>
/// <param name="Description">When to use it.</param>
/// <param name="Files">Additional files, relative to the skill.</param>
public sealed record SkillSummary(string Name, string Description, IReadOnlyList<string> Files);

/// <summary>A loaded agent template.</summary>
/// <param name="Name">Template name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="ModelTier">Fixed model tier; null = by input mode.</param>
/// <param name="Tools">Tool id globs it may use.</param>
/// <param name="Skills">Skills it may use; null = all.</param>
/// <param name="MaxSteps">Step limit; null = <c>agent.maxSteps</c>.</param>
/// <param name="MaxAdvisorCalls">Advisor calls per run; null = <c>agent.advisor.maxCallsPerRun</c>.</param>
/// <param name="BuiltIn">True for the built-in default (no <c>default.md</c>).</param>
public sealed record TemplateSummary(
    string Name, string Description, ModelTier? ModelTier, IReadOnlyList<string> Tools, IReadOnlyList<string>? Skills, int? MaxSteps, int? MaxAdvisorCalls, bool BuiltIn);

/// <summary>A configured MCP server.</summary>
/// <param name="Id">Server id.</param>
/// <param name="Namespace">Tool namespace (id or <c>toolPrefix</c>).</param>
/// <param name="Transport"><c>http</c> or <c>stdio</c>.</param>
/// <param name="Target">URL (http) or command line (stdio).</param>
/// <param name="Untrusted">Its tool output taints the run.</param>
/// <param name="AuthTokenSecret">Secret name of the bearer token, if any.</param>
/// <param name="EnvironmentSecrets">Secret names passed as environment variables (stdio).</param>
/// <param name="State">Connection state.</param>
/// <param name="Error">Last connection error, if any.</param>
/// <param name="ToolCount">Tools offered while connected.</param>
/// <param name="File">Config file, relative to <c>/config/mcp</c>.</param>
/// <param name="UntrustedTools">Globs on reported tool names whose output taints the run (with <paramref name="Untrusted"/> false).</param>
/// <param name="Warnings">Problems that leave the server usable, e.g. an <c>untrustedTools</c> glob that matches no tool.</param>
public sealed record McpServerSummary(
    string Id, string Namespace, string Transport, string Target, bool Untrusted, string? AuthTokenSecret, IReadOnlyList<string> EnvironmentSecrets,
    McpServerState State, string? Error, int ToolCount, string File, IReadOnlyList<string> UntrustedTools, IReadOnlyList<string> Warnings);

/// <summary>An active risk rule.</summary>
/// <param name="Tool">Tool id glob.</param>
/// <param name="Match">Target attribute conditions (e.g. <c>domain</c>, <c>area</c>).</param>
/// <param name="Args">Argument conditions.</param>
/// <param name="Tier">Tier when the rule matches.</param>
public sealed record RiskRuleSummary(
    string Tool, IReadOnlyDictionary<string, IReadOnlyList<string>> Match, IReadOnlyDictionary<string, IReadOnlyList<string>> Args, RiskTier Tier);

/// <summary>A configured model.</summary>
/// <param name="Key">Model key in <c>marshal.yaml</c>.</param>
/// <param name="Tier">Model tier.</param>
/// <param name="Provider">Provider.</param>
/// <param name="Model">Provider model name.</param>
/// <param name="Endpoint">Server URL, if any.</param>
/// <param name="ApiKeySecret">Secret name of the API key, if any.</param>
/// <param name="Vision">Accepts images.</param>
/// <param name="Tools">Supports tool calls.</param>
/// <param name="ContextLength">Context window in tokens.</param>
public sealed record ModelSummary(
    string Key, ModelTier Tier, string Provider, string Model, string? Endpoint, string? ApiKeySecret, bool Vision, bool Tools, int ContextLength);

/// <summary>A satellite profile.</summary>
/// <param name="Id">Satellite entity id.</param>
/// <param name="Verbosity">Reply verbosity.</param>
/// <param name="Notes">Operator notes.</param>
/// <param name="DenyTools">Tool globs hidden on this satellite.</param>
/// <param name="DenyConfirm">Tool globs whose confirm-tier calls are refused on this satellite.</param>
public sealed record SatelliteSummary(string Id, string Verbosity, string? Notes, IReadOnlyList<string> DenyTools, IReadOnlyList<string> DenyConfirm);
