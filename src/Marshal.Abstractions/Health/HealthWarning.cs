namespace Marshal.Abstractions.Health;

/// <summary>How bad a health warning is.</summary>
public enum HealthSeverity
{
    /// <summary>Marshal keeps working with less (e.g. one MCP server is down).</summary>
    Warning,

    /// <summary>A core function fails (e.g. the database is unreachable).</summary>
    Error,
}

/// <summary>
/// A message authored by Marshal in its two languages. Never contains text received from external systems or secret
/// values, so it may be shown to the model as trusted output.
/// </summary>
/// <param name="De">German text.</param>
/// <param name="En">English text.</param>
public sealed record HealthText(string De, string En)
{
    /// <summary>The text for <paramref name="language"/> (<c>de</c> or English otherwise).</summary>
    public string For(string language) => string.Equals(language, "de", StringComparison.OrdinalIgnoreCase) ? De : En;
}

/// <summary>
/// A problem a component reports right now. <see cref="ISystemHealth"/> adds the time it first appeared.
/// </summary>
/// <param name="Component">What is affected, e.g. <c>database</c>, <c>mcp:paperless</c>, <c>config:risk.yaml</c>.</param>
/// <param name="Code">Stable code, e.g. <c>mcp.unreachable</c>.</param>
/// <param name="Severity">How bad it is.</param>
/// <param name="Message">Marshal-authored message.</param>
/// <param name="Link">Path in the web UI that shows details (e.g. <c>/system/config</c>), if any.</param>
public sealed record HealthCondition(string Component, string Code, HealthSeverity Severity, HealthText Message, string? Link = null);

/// <summary>A current health warning.</summary>
/// <param name="Component">What is affected.</param>
/// <param name="Code">Stable code.</param>
/// <param name="Severity">How bad it is.</param>
/// <param name="Message">Marshal-authored message.</param>
/// <param name="Link">Path in the web UI that shows details, if any.</param>
/// <param name="Since">When the warning (same component and code) first appeared.</param>
public sealed record HealthWarning(string Component, string Code, HealthSeverity Severity, HealthText Message, string? Link, DateTimeOffset Since);
