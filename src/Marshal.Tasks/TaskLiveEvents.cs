using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks;

/// <summary>
/// Live-event topics of the task system.
/// </summary>
public static class TaskLiveEvents
{
    /// <summary>Task created or changed (<c>task.changed</c>).</summary>
    public const string Tasks = "tasks";

    /// <summary>Every notification (<c>notification.created</c>, <c>notification.updated</c>), whatever its channel.</summary>
    public const string Notifications = "notifications";
}

/// <summary>Payload of <c>task.changed</c>.</summary>
public sealed record TaskChangedPayload(TaskId TaskId, string? Name, AgentTaskStatus Status, string? StatusReason, DateTimeOffset? NextRunAt, DateTimeOffset? ResultAt);

/// <summary>Payload of <c>notification.created</c> and <c>notification.updated</c>.</summary>
public sealed record NotificationPayload(
    Guid Id,
    string Source,
    NotificationPriority Priority,
    string Text,
    NotificationOutcome Outcome,
    string? Channel,
    string? Reason,
    TaskId? TaskId,
    DateTimeOffset CreatedAt);
