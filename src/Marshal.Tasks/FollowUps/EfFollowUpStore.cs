using System.Buffers.Text;
using System.Security.Cryptography;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Sessions;
using Marshal.Data;
using Marshal.Data.Tasks;
using Marshal.Tasks.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.FollowUps;

/// <summary>
/// <see cref="IFollowUpStore"/> on the <c>follow_ups</c> table, scoped to the current owner. Nonces are 128 random
/// bits; a follow-up lives for <see cref="NotificationOptions.FollowUpTtl"/> and is consumed by one reply (one
/// conditional update, so two turns cannot both take it).
/// </summary>
internal sealed class EfFollowUpStore(
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IOptions<NotificationOptions> options,
    TimeProvider time) : IFollowUpStore
{
    public async Task<FollowUpRecord> CreateAsync(NewFollowUp followUp, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        var row = new FollowUp
        {
            Id = Guid.CreateVersion7(),
            OwnerId = executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Follow-ups created without an execution context."),
            Nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
            SatelliteId = followUp.SatelliteId,
            TaskId = followUp.TaskId,
            PendingActionId = followUp.PendingActionId,
            CreatedAt = now,
            ExpiresAt = now + options.Value.FollowUpTtl,
        };

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        db.FollowUps.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    public async Task<FollowUpRecord?> ConsumeAsync(string nonce, string satelliteId, SessionId session, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        Guid? id = await Open(db, satelliteId, now).Where(row => row.Nonce == nonce).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(cancellationToken);
        return id is null ? null : await ConsumeAsync(db, id.Value, session, now, cancellationToken);
    }

    public async Task<FollowUpRecord?> ConsumeRecentAsync(string satelliteId, TimeSpan window, SessionId session, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset since = now - window;
        Guid? id = await Open(db, satelliteId, now)
            .Where(row => row.CreatedAt >= since)
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id is null ? null : await ConsumeAsync(db, id.Value, session, now, cancellationToken);
    }

    private static IQueryable<FollowUp> Open(MarshalDbContext db, string satelliteId, DateTimeOffset now) =>
        db.FollowUps.Where(row => row.SatelliteId == satelliteId && row.ConsumedAt == null && row.ExpiresAt > now);

    private static async Task<FollowUpRecord?> ConsumeAsync(MarshalDbContext db, Guid id, SessionId session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        int consumed = await db.FollowUps
            .Where(row => row.Id == id && row.ConsumedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.ConsumedAt, now).SetProperty(row => row.LinkedSessionId, session), cancellationToken);
        if (consumed == 0)
            return null;

        FollowUp row = await db.FollowUps.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);
        return ToRecord(row);
    }

    private static FollowUpRecord ToRecord(FollowUp row) =>
        new(row.Id, row.Nonce, row.SatelliteId, row.TaskId, row.PendingActionId, row.ExpiresAt, row.ConsumedAt);
}
