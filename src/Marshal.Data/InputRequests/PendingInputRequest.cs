using Marshal.Abstractions.Identity;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;

namespace Marshal.Data.InputRequests;

/// <summary>
/// An MCP server's input request with the interrupted call frozen (<c>pending_input_requests</c>).
/// </summary>
public sealed class PendingInputRequest : IOwned
{
    public required InputRequestId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public SessionId? SessionId { get; init; }

    public string? SatelliteId { get; init; }

    public TaskId? TaskId { get; init; }

    /// <summary>The run that asked (no foreign key: runs are purged by retention).</summary>
    public RunId? RunId { get; init; }

    public required string ServerId { get; init; }

    /// <summary>Internal tool id.</summary>
    public required string Tool { get; init; }

    /// <summary>Frozen arguments (JSON object).</summary>
    public required string Args { get; init; }

    /// <summary>Answers to earlier requests of the same call (JSON array).</summary>
    public required string PreviousAnswers { get; init; }

    public required InputRequestMode Mode { get; init; }

    /// <summary>Server text: shown only in the web UI.</summary>
    public required string Message { get; init; }

    public string? RequestedSchema { get; init; }

    public string? Url { get; init; }

    /// <summary>The server's task id when a running server task asks; the answer goes to it with <c>tasks/update</c>.</summary>
    public string? ServerTaskId { get; init; }

    /// <summary>Key of the request in the server task's <c>inputRequests</c>.</summary>
    public string? ServerTaskInputKey { get; init; }

    public required string Nonce { get; init; }

    public required string Language { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public InputRequestStatus Status { get; set; } = InputRequestStatus.Pending;

    public DateTimeOffset? DecidedAt { get; set; }
}
