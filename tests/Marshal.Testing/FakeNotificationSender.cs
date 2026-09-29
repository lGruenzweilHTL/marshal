using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Notifications;

namespace Marshal.Testing;

/// <summary>
/// Records sent notifications.
/// </summary>
public sealed class FakeNotificationSender : INotificationSender
{
    public ConcurrentQueue<NotificationRequest> Sent { get; } = new();

    public Task<Result<Guid>> SendAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        Sent.Enqueue(request);
        return Task.FromResult<Result<Guid>>(Guid.NewGuid());
    }
}
