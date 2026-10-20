using Marshal.Abstractions.Apps;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;

namespace Marshal.Data.Apps;

/// <summary>
/// The MCP app one tool call showed in a web turn (<c>app_instances</c>): resource HTML, arguments and result, so
/// the app renders again when the conversation is reopened.
/// </summary>
public sealed class AppInstance : IOwned
{
    public required AppInstanceId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public SessionId? SessionId { get; init; }

    /// <summary>The run of the call (no foreign key: runs are purged by retention).</summary>
    public required RunId RunId { get; init; }

    public required string Server { get; init; }

    /// <summary>The tool name as the server reports it.</summary>
    public required string Tool { get; init; }

    public required string ToolCallId { get; init; }

    public required string ResourceUri { get; init; }

    /// <summary>Server-authored HTML, at most 1 MB; served only to a sandboxed iframe.</summary>
    public required string Html { get; init; }

    /// <summary>The call's arguments (JSON object).</summary>
    public required string Args { get; init; }

    /// <summary>The call's <c>CallToolResult</c> JSON; null when it was too large to keep.</summary>
    public string? Result { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
