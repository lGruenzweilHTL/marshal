using System.Reflection;
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
            .WithSummary("The app version and the supported and deprecated API versions.");
        return group;
    }

    private static Ok<SystemInfo> GetSystem() =>
        TypedResults.Ok(new SystemInfo(
            AppVersion,
            [.. ApiVersions.Supported.Select(version => version.ToString())],
            [.. ApiVersions.Deprecated.Select(version => version.ToString())]));
}

/// <summary>Version information.</summary>
/// <param name="Version">App version (informational version, including the commit when built from source).</param>
/// <param name="SupportedApiVersions">API versions this build serves, e.g. <c>1.0</c>.</param>
/// <param name="DeprecatedApiVersions">Supported versions that will be removed.</param>
public sealed record SystemInfo(string Version, IReadOnlyList<string> SupportedApiVersions, IReadOnlyList<string> DeprecatedApiVersions);
