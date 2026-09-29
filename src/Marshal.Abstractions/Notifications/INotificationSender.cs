namespace Marshal.Abstractions.Notifications;

/// <summary>
/// Sends a notification to a person. The router picks the channel (priority, quiet hours, DND, presence,
/// coalescing, voice caps) and records the decision.
/// </summary>
public interface INotificationSender
{
    /// <summary>
    /// Records and routes <paramref name="request"/>; returns the notification id. <c>critical</c> from a source that
    /// is not system-defined fails validation.
    /// </summary>
    Task<Result<Guid>> SendAsync(NotificationRequest request, CancellationToken cancellationToken);
}
