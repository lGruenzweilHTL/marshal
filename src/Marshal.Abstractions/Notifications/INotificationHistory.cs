using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Notifications;

/// <summary>
/// The current owner's notifications with the router's decisions, for the notification history.
/// </summary>
public interface INotificationHistory
{
    /// <summary>Matching notifications, newest first.</summary>
    Task<IReadOnlyList<NotificationEntry>> ListAsync(NotificationQuery query, CancellationToken cancellationToken);
}

/// <summary>Which notifications to list. Null criteria match every notification.</summary>
/// <param name="Outcome">Only this outcome.</param>
/// <param name="TaskId">Only notifications of this task.</param>
/// <param name="Before">Only notifications created before this time (paging).</param>
/// <param name="Limit">At most this many (1 to 200).</param>
public sealed record NotificationQuery(NotificationOutcome? Outcome = null, TaskId? TaskId = null, DateTimeOffset? Before = null, int Limit = 50);

/// <summary>A notification and what the router did with it.</summary>
/// <param name="Id">Notification id.</param>
/// <param name="TaskId">The task that sent it, if any.</param>
/// <param name="Source">Who sent it, e.g. <c>task:&lt;id&gt;</c> or <c>system:babysitter</c>.</param>
/// <param name="Priority">Requested priority.</param>
/// <param name="Content">The text.</param>
/// <param name="Channel">Channel it was delivered on.</param>
/// <param name="Outcome">The router's decision.</param>
/// <param name="Reason">Why, e.g. <c>quiet_hours</c> or <c>voice_cap</c>.</param>
/// <param name="CoalescedInto">The notification it was merged into.</param>
/// <param name="CreatedAt">When it was sent.</param>
/// <param name="DeliveredAt">When it was delivered.</param>
public sealed record NotificationEntry(
    Guid Id,
    TaskId? TaskId,
    string Source,
    NotificationPriority Priority,
    string Content,
    string? Channel,
    NotificationOutcome Outcome,
    string? Reason,
    Guid? CoalescedInto,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt);
