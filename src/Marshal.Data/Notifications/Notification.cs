using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;

namespace Marshal.Data.Notifications;

/// <summary>
/// A notification with the router's decision; also the inbox (outcome <c>inboxed</c>).
/// </summary>
public sealed class Notification : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public TaskId? TaskId { get; init; }

    /// <summary>Who sent it, e.g. <c>task:&lt;id&gt;</c> or <c>system:babysitter</c>.</summary>
    public required string Source { get; init; }

    public required NotificationPriority Priority { get; init; }

    public required string Content { get; init; }

    /// <summary>Channel it was delivered on.</summary>
    public string? Channel { get; set; }

    public NotificationOutcome Outcome { get; set; } = NotificationOutcome.Pending;

    /// <summary>Why the router decided as it did (e.g. <c>voice_cap</c>).</summary>
    public string? Reason { get; set; }

    public Guid? CoalescedInto { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>When a task consumed the inbox item.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}
