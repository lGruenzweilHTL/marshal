using System.Text.Json;

namespace Marshal.Abstractions.Tools;

/// <summary>
/// What the registry, the guard and the recorder need to know about a tool.
/// </summary>
public sealed record ToolDescriptor
{
    /// <summary>Internal id.</summary>
    public required ToolId Id { get; init; }

    /// <summary>Description shown to the model.</summary>
    public required string Description { get; init; }

    /// <summary>JSON schema of the arguments object.</summary>
    public required JsonElement ParametersSchema { get; init; }

    /// <summary>Tier used when no risk rule matches.</summary>
    public RiskTier DefaultTier { get; init; } = RiskTier.Safe;

    /// <summary>Output is third-party content; it taints the run and is wrapped as untrusted data.</summary>
    public bool UntrustedOutput { get; init; }

    /// <summary>Taint source kind recorded when <see cref="UntrustedOutput"/> is set (defaults to the namespace).</summary>
    public string? TaintKind { get; init; }

    /// <summary>Persists state the agent acts on later; escalated to confirm in tainted runs.</summary>
    public bool Persists { get; init; }

    /// <summary>Results are recorded as references only, never as content.</summary>
    public bool Sensitive { get; init; }
}
