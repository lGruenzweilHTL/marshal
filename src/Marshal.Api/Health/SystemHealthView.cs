using Marshal.Abstractions.Health;

namespace Marshal.Api.Health;

/// <summary>The current health warnings.</summary>
/// <param name="Warnings">Errors first, then by component and code; empty when everything is fine.</param>
public sealed record SystemHealthView(IReadOnlyList<HealthWarningView> Warnings)
{
    internal static SystemHealthView From(IReadOnlyList<HealthWarning> warnings) =>
        new([.. warnings.Select(warning => new HealthWarningView(
            warning.Component,
            warning.Code,
            warning.Severity,
            new HealthMessageView(warning.Message.De, warning.Message.En),
            warning.Link,
            warning.Since))]);
}

/// <summary>One health warning.</summary>
/// <param name="Component">What is affected, e.g. <c>database</c>, <c>mcp:paperless</c>, <c>config:risk.yaml</c>.</param>
/// <param name="Code">Stable code, e.g. <c>mcp.unreachable</c>, <c>mcp.tool_pending</c>.</param>
/// <param name="Severity"><c>warning</c> or <c>error</c>.</param>
/// <param name="Message">Marshal-authored message in German and English; never text from external systems.</param>
/// <param name="Link">Path in the web UI with details (e.g. <c>/system/config</c>), if any.</param>
/// <param name="Since">When the warning first appeared.</param>
public sealed record HealthWarningView(
    string Component,
    string Code,
    HealthSeverity Severity,
    HealthMessageView Message,
    string? Link,
    DateTimeOffset Since);

/// <summary>A message in Marshal's two languages.</summary>
/// <param name="De">German.</param>
/// <param name="En">English.</param>
public sealed record HealthMessageView(string De, string En);
