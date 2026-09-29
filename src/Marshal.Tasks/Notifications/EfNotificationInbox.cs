using Marshal.Abstractions.Notifications;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.Notifications;

/// <summary>
/// <see cref="INotificationInbox"/> on the <c>notifications</c> table, scoped to the current owner by the context's
/// query filter.
/// </summary>
internal sealed class EfNotificationInbox(IDbContextFactory<MarshalDbContext> contexts, TimeProvider time) : INotificationInbox
{
    public async Task<IReadOnlyList<InboxItem>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Notifications.AsNoTracking()
            .Where(row => row.Outcome == NotificationOutcome.Inboxed && row.ConsumedAt == null)
            .OrderByDescending(row => row.CreatedAt)
            .Take(limit)
            .Select(row => new InboxItem(row.Id, row.Source, row.Priority, row.Content, row.CreatedAt, row.TaskId))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ConsumeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Notifications
            .Where(row => ids.Contains(row.Id) && row.Outcome == NotificationOutcome.Inboxed && row.ConsumedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConsumedAt, now), cancellationToken);
    }
}
