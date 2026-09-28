using System.Text.Json;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;

namespace Marshal.Data.Audit;

/// <summary>
/// Audit events in the <c>audit_events</c> table, owned by the current execution context's owner.
/// </summary>
public sealed class DbAuditLog(MarshalDbContext db, IExecutionContextAccessor executionContext, TimeProvider timeProvider) : IAuditLog
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = executionContext.Current
            ?? throw new InvalidOperationException("Audit events require an execution context.");
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            OwnerId = context.OwnerId,
            ActorKind = entry.ActorKind,
            ActorId = entry.ActorId,
            RunId = entry.RunId,
            Action = entry.Action,
            Target = entry.Target,
            Details = entry.Details is null ? null : JsonSerializer.Serialize(entry.Details),
            CreatedAt = timeProvider.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
