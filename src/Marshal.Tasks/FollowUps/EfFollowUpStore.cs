using System.Buffers.Text;
using System.Security.Cryptography;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.FollowUps;
using Marshal.Data;
using Marshal.Data.Tasks;
using Marshal.Tasks.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.FollowUps;

/// <summary>
/// <see cref="IFollowUpStore"/> on the <c>follow_ups</c> table, scoped to the current owner. Nonces are 128 random
/// bits; a follow-up lives for <see cref="NotificationOptions.FollowUpTtl"/>.
/// </summary>
internal sealed class EfFollowUpStore(
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IOptions<NotificationOptions> options,
    TimeProvider time) : IFollowUpStore
{
    public async Task<FollowUpRecord> CreateAsync(NewFollowUp followUp, CancellationToken cancellationToken)
    {
        var row = new FollowUp
        {
            Id = Guid.CreateVersion7(),
            OwnerId = executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Follow-ups created without an execution context."),
            Nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
            SatelliteId = followUp.SatelliteId,
            TaskId = followUp.TaskId,
            PendingActionId = followUp.PendingActionId,
            ExpiresAt = time.GetUtcNow() + options.Value.FollowUpTtl,
        };

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        db.FollowUps.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    private static FollowUpRecord ToRecord(FollowUp row) =>
        new(row.Id, row.Nonce, row.SatelliteId, row.TaskId, row.PendingActionId, row.ExpiresAt, row.ConsumedAt);
}
