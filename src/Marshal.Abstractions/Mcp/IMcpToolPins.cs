using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Mcp;

/// <summary>
/// Pinned MCP tool definitions. Instance configuration: no owner, readable without an execution context.
/// </summary>
public interface IMcpToolPins
{
    /// <summary>Every pin of one server.</summary>
    Task<IReadOnlyList<McpToolPin>> GetAsync(string serverId, CancellationToken cancellationToken);

    /// <summary>Pins waiting for a decision, by server and tool.</summary>
    Task<IReadOnlyList<McpToolPin>> ListPendingAsync(CancellationToken cancellationToken);

    /// <summary>Inserts or replaces the pin of <see cref="McpToolPin.ServerId"/> and <see cref="McpToolPin.ToolName"/>.</summary>
    Task SaveAsync(IReadOnlyList<McpToolPin> pins, CancellationToken cancellationToken);

    /// <summary>
    /// Approves or rejects a pending pin. Fails with <c>mcp.pin_not_found</c> when there is no pin and with
    /// <c>mcp.pin_not_pending</c> when it is not waiting for a decision.
    /// </summary>
    Task<Result<McpToolPin>> DecideAsync(string serverId, string toolName, bool approve, OwnerId actor, DateTimeOffset at, CancellationToken cancellationToken);
}

/// <summary>Whether a waiting tool is new or changed.</summary>
public enum McpToolChange
{
    /// <summary>The server reported a tool without a pin after its first discovery.</summary>
    New,

    /// <summary>The tool's definition differs from the approved one.</summary>
    Changed,
}

/// <summary>
/// The pin of one MCP tool.
/// </summary>
/// <param name="ServerId">MCP server id.</param>
/// <param name="ToolName">Tool name as the server reports it.</param>
/// <param name="Fingerprint">SHA-256 of the latest definition the server reported.</param>
/// <param name="State">Decision on <paramref name="Fingerprint"/>.</param>
/// <param name="ApprovedFingerprint">The last approved fingerprint; null for a new tool never approved.</param>
/// <param name="PreviousDefinitionJson">Canonical JSON of the last approved definition while a change waits; null for a new tool.</param>
/// <param name="CurrentDefinitionJson">Canonical JSON of the latest definition.</param>
/// <param name="FirstSeen">When the latest definition was first reported.</param>
/// <param name="DecidedAt">When an admin decided; null for trust on first use and while pending.</param>
/// <param name="DecidedBy">Who decided.</param>
public sealed record McpToolPin(
    string ServerId,
    string ToolName,
    string Fingerprint,
    ToolPinState State,
    string? ApprovedFingerprint,
    string? PreviousDefinitionJson,
    string CurrentDefinitionJson,
    DateTimeOffset FirstSeen,
    DateTimeOffset? DecidedAt = null,
    OwnerId? DecidedBy = null)
{
    /// <summary>New when no definition of the tool was ever approved.</summary>
    public McpToolChange Change => ApprovedFingerprint is null ? McpToolChange.New : McpToolChange.Changed;
}
