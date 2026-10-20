using Marshal.Abstractions.Artifacts;

namespace Marshal.Abstractions.Tools;

/// <summary>
/// Successful tool result.
/// </summary>
/// <param name="Content">Text given to the model.</param>
/// <param name="Reference">Stable reference to the content (message id, path) recorded instead of the content for sensitive tools.</param>
public sealed record ToolOutput(string Content, string? Reference = null)
{
    /// <summary>Artifacts the call shows in the turn (e.g. <c>artifacts.show</c>); each becomes an <c>artifact</c> event.</summary>
    public IReadOnlyList<ArtifactRef>? Artifacts { get; init; }

    /// <summary>The MCP Apps UI the call shows in a web turn; becomes an <c>app</c> event.</summary>
    public Apps.McpAppOutput? App { get; init; }

    /// <summary>
    /// The MCP <c>CallToolResult</c> as JSON, for callers that pass it on unchanged (MCP apps); null for built-in
    /// tools and for results above <see cref="Apps.McpAppOutput.MaxResultBytes"/>.
    /// </summary>
    public string? CallResult { get; init; }

    /// <summary>
    /// Taint source when this particular result is third-party content although the tool's descriptor is not
    /// <see cref="ToolDescriptor.UntrustedOutput"/> (e.g. a free-text <c>agent.extract</c> answer). The result taints the
    /// run and is wrapped as untrusted data, like the output of an untrusted tool.
    /// </summary>
    public string? TaintKind { get; init; }

    /// <summary>Notes recorded with the call's step (for example an app resource that was skipped).</summary>
    public IReadOnlyList<Context.ContextNote>? Notes { get; init; }
}
