using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;

namespace Marshal.Data.Tasks;

/// <summary>
/// A voice question or delivery that expects a reply. Its nonce travels as <c>extra_system_prompt</c> and comes back
/// as the turn's <c>followupRef</c>.
/// </summary>
public sealed class FollowUp : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required string Nonce { get; init; }

    public required string SatelliteId { get; init; }

    public PendingActionId? PendingActionId { get; init; }

    public TaskId? TaskId { get; init; }

    /// <summary>Session the reply was linked to.</summary>
    public SessionId? LinkedSessionId { get; set; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? ConsumedAt { get; set; }
}
