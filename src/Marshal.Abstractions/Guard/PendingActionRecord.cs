using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// A frozen confirm-tier call. On confirmation exactly this tool and these arguments are executed.
/// </summary>
public sealed record PendingActionRecord
{
    public required PendingActionId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public SessionId? SessionId { get; init; }

    public string? SatelliteId { get; init; }

    /// <summary>Background approvals: the waiting task; null for queued approvals of runs without a task.</summary>
    public TaskId? TaskId { get; init; }

    /// <summary>In the approval queue (background and taskless approvals), not a session's pending action.</summary>
    public bool Queued { get; init; }

    /// <summary>The run that asked.</summary>
    public RunId? RunId { get; init; }

    /// <summary>Untrusted sources of that run, with their first steps.</summary>
    public IReadOnlyList<TaintSource> Taint { get; init; } = [];

    public required ToolId Tool { get; init; }

    /// <summary>Frozen arguments object, as JSON text.</summary>
    public required string ArgumentsJson { get; init; }

    public required string Summary { get; init; }

    /// <summary>Reasons, the spoken one first.</summary>
    public required IReadOnlyList<ConfirmationReason> Reasons { get; init; }

    public required string Nonce { get; init; }

    public required string Language { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public PendingActionStatus Status { get; init; } = PendingActionStatus.Pending;
}
