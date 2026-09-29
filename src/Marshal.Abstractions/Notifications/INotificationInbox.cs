using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Notifications;

/// <summary>
/// The current owner's inbox: notifications with outcome <c>inboxed</c> that no task has consumed yet.
/// </summary>
public interface INotificationInbox
{
    /// <summary>Unconsumed items, newest first.</summary>
    Task<IReadOnlyList<InboxItem>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Marks the owner's items as consumed; returns how many were still unconsumed.</summary>
    Task<int> ConsumeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>An inbox item. <paramref name="Text"/> may carry third-party text.</summary>
public sealed record InboxItem(Guid Id, string Source, NotificationPriority Priority, string Text, DateTimeOffset CreatedAt, TaskId? TaskId);
