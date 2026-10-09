using System.Reflection;
using Marshal.Abstractions.Events;
using Marshal.Api.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Health;

/// <summary>
/// <c>/system</c>: version information for the system view. Health details stay on the unversioned <c>/health</c>.
/// </summary>
internal static class SystemEndpoints
{
    private static readonly string AppVersion =
        typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/system", GetSystem).RequireAuthorization().WithTags("System").WithName("GetSystem")
            .WithSummary("The app version, the supported and deprecated API versions and the event sources.");
        return group;
    }

    private static Ok<SystemInfo> GetSystem(IEnumerable<IEventSource> sources, IWebhookIngest webhooks) =>
        TypedResults.Ok(new SystemInfo(
            AppVersion,
            [.. ApiVersions.Supported.Select(version => version.ToString())],
            [.. ApiVersions.Deprecated.Select(version => version.ToString())],
            [.. sources.OrderBy(source => source.Source, StringComparer.Ordinal).Select(source => new EventSourceInfo(source.Source, source.IsConnected, webhooks.Sources.Contains(source.Source, StringComparer.Ordinal), source.LastReceivedAt))]));
}

/// <summary>Version information.</summary>
/// <param name="Version">App version (informational version, including the commit when built from source).</param>
/// <param name="SupportedApiVersions">API versions this build serves, e.g. <c>1.0</c>.</param>
/// <param name="DeprecatedApiVersions">Supported versions that will be removed.</param>
/// <param name="EventSources">What publishes events on the event bus (HA, printers, mail, webhook sources).</param>
public sealed record SystemInfo(string Version, IReadOnlyList<string> SupportedApiVersions, IReadOnlyList<string> DeprecatedApiVersions, IReadOnlyList<EventSourceInfo> EventSources);

/// <summary>An event source and its state.</summary>
/// <param name="Source">Source name, e.g. <c>ha</c>, <c>uptime-kuma</c>.</param>
/// <param name="Connected">Whether it currently receives events (webhook sources: a webhook arrived in the last 24 hours).</param>
/// <param name="Webhook">Whether it is a webhook source (<c>POST /api/v1/hooks/{source}</c>).</param>
/// <param name="LastReceivedAt">When it last received a webhook; null for other sources or before the first one.</param>
public sealed record EventSourceInfo(string Source, bool Connected, bool Webhook, DateTimeOffset? LastReceivedAt);
