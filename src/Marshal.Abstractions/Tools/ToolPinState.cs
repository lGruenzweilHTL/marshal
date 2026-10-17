namespace Marshal.Abstractions.Tools;

/// <summary>
/// Whether an admin accepted a tool's current definition. Only MCP tools are pinned; every other tool is
/// <see cref="Approved"/>.
/// </summary>
public enum ToolPinState
{
    /// <summary>Trusted: offered to the model and callable.</summary>
    Approved,

    /// <summary>New or changed since the last approval: hidden and denied until an admin decides.</summary>
    Pending,

    /// <summary>Rejected by an admin: hidden and denied until the definition changes again.</summary>
    Rejected,
}
