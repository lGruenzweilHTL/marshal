using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;

namespace Marshal.Data.Conversations;

/// <summary>
/// A confirm-tier call frozen server-side until a user action decides it.
/// </summary>
public sealed class PendingAction : IOwned
{
    public required PendingActionId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public SessionId? SessionId { get; init; }

    public Guid? TaskId { get; init; }

    public string? SatelliteId { get; init; }

    /// <summary>Internal tool id.</summary>
    public required string Tool { get; init; }

    /// <summary>Frozen arguments (JSON object).</summary>
    public required string Args { get; init; }

    public required string Summary { get; init; }

    /// <summary>JSON array of reasons: code + localized sentence.</summary>
    public required string Reasons { get; init; }

    public required string Nonce { get; init; }

    /// <summary>Language the reasons are phrased in.</summary>
    public required string Language { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public PendingActionStatus Status { get; set; } = PendingActionStatus.Pending;

    public DecidedBy? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}
