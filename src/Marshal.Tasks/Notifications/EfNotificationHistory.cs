using Marshal.Abstractions.Notifications;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.Notifications;

/// <summary>
/// <see cref="INotificationHistory"/> on the <c>notifications</c> table, scoped to the current owner by the context's
/// query filter.
/// </summary>
internal sealed class EfNotificationHistory(IDbContextFactory<MarshalDbContext> contexts) : INotificationHistory
{
    private const int MaxLimit = 200;

    public async Task<IReadOnlyList<NotificationEntry>> ListAsync(NotificationQuery query, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<Data.Notifications.Notification> rows = db.Notifications.AsNoTracking();
        if (query.Outcome is { } outcome)
            rows = rows.Where(row => row.Outcome == outcome);
        if (query.TaskId is { } task)
            rows = rows.Where(row => row.TaskId == task);
        if (query.Before is { } before)
            rows = rows.Where(row => row.CreatedAt < before);

        return await rows.OrderByDescending(row => row.CreatedAt)
            .Take(Math.Clamp(query.Limit, 1, MaxLimit))
            .Select(row => new NotificationEntry(row.Id, row.TaskId, row.Source, row.Priority, row.Content, row.Channel, row.Outcome, row.Reason, row.CoalescedInto, row.CreatedAt, row.DeliveredAt))
            .ToListAsync(cancellationToken);
    }
}
