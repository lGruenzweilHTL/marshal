using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;

namespace Marshal.Data.Mcp;

/// <summary>
/// The pin of one MCP tool (<c>mcp_tool_pins</c>). Instance configuration like the server files: no owner column.
/// </summary>
public sealed class McpToolPinRow
{
    public required string ServerId { get; init; }

    public required string ToolName { get; init; }

    public required string Fingerprint { get; set; }

    public required ToolPinState State { get; set; }

    public string? ApprovedFingerprint { get; set; }

    public string? PreviousDefinitionJson { get; set; }

    public required string CurrentDefinitionJson { get; set; }

    public required DateTimeOffset FirstSeen { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public OwnerId? DecidedBy { get; set; }
}
