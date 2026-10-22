using System.Reflection;
using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Health;
using Marshal.Api.Auth;
using Marshal.Api.Versioning;
using Marshal.Demo;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Health;

/// <summary>
/// <c>/system</c>: version information for the system view; <c>/system/health</c>: coded health warnings for the
/// banner. The unversioned <c>/health</c> stays the probe for orchestrators.
/// </summary>
internal static class SystemEndpoints
{
    private static readonly string AppVersion =
        typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/system", GetSystem).RequireAuthorization().WithTags("System").WithName("GetSystem")
            .WithSummary("The app version, the supported and deprecated API versions and the event sources.");
        group.MapGet("/system/health", GetHealthAsync).RequireAuthorization(Policies.HumanOrAdmin).WithTags("System").WithName("GetSystemHealth")
            .WithSummary("The current health warnings (codes, components, messages in German and English). Changes are published on the health live topic.");
        return group;
    }

    private static Ok<SystemInfo> GetSystem(IEnumerable<IEventSource> sources, IWebhookIngest webhooks, DemoIntegrations demo) =>
        TypedResults.Ok(new SystemInfo(
            AppVersion,
            [.. ApiVersions.Supported.Select(version => version.ToString())],
            [.. ApiVersions.Deprecated.Select(version => version.ToString())],
            [.. sources.OrderBy(source => source.Source, StringComparer.Ordinal).Select(source => new EventSourceInfo(source.Source, source.IsConnected, webhooks.Sources.Contains(source.Source, StringComparer.Ordinal), source.LastReceivedAt))],
            [.. demo.SimulatedAtStartup.Select(integration => JsonNamingPolicy.SnakeCaseLower.ConvertName(integration.ToString()))]));

    private static async Task<Ok<SystemHealthView>> GetHealthAsync(ISystemHealth health, CancellationToken cancellationToken) =>
        TypedResults.Ok(SystemHealthView.From(await health.GetWarningsAsync(cancellationToken)));
}

/// <summary>Version information.</summary>
/// <param name="Version">App version (informational version, including the commit when built from source).</param>
/// <param name="SupportedApiVersions">API versions this build serves, e.g. <c>1.0</c>.</param>
/// <param name="DeprecatedApiVersions">Supported versions that will be removed.</param>
/// <param name="EventSources">What publishes events on the event bus (HA, printers, mail, webhook sources).</param>
/// <param name="SimulatedIntegrations">Integrations demo mode simulates (<c>home_assistant</c>, <c>caldav</c>, <c>printers</c>, <c>mail</c>, <c>rss</c>, <c>notes</c>, <c>web</c>); empty without demo mode.</param>
public sealed record SystemInfo(string Version, IReadOnlyList<string> SupportedApiVersions, IReadOnlyList<string> DeprecatedApiVersions, IReadOnlyList<EventSourceInfo> EventSources, IReadOnlyList<string> SimulatedIntegrations);

/// <summary>An event source and its state.</summary>
/// <param name="Source">Source name, e.g. <c>ha</c>, <c>uptime-kuma</c>.</param>
/// <param name="Connected">Whether it currently receives events (webhook sources: a webhook arrived in the last 24 hours).</param>
/// <param name="Webhook">Whether it is a webhook source (<c>POST /api/v1/hooks/{source}</c>).</param>
/// <param name="LastReceivedAt">When it last received a webhook; null for other sources or before the first one.</param>
public sealed record EventSourceInfo(string Source, bool Connected, bool Webhook, DateTimeOffset? LastReceivedAt);
