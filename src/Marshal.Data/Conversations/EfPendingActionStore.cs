using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Conversations;

/// <summary>
/// Pending actions in the <c>pending_actions</c> table, scoped to the current owner.
/// </summary>
public sealed class EfPendingActionStore(MarshalDbContext db, IExecutionContextAccessor executionContext, TimeProvider timeProvider) : IPendingActionStore
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task<PendingActionRecord> FreezeAsync(FreezeRequest request, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = executionContext.Current
            ?? throw new InvalidOperationException("Freezing an action requires an execution context.");
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (request.SessionId is { } sessionId)
        {
            foreach (PendingAction previous in await db.PendingActions.Where(action => action.SessionId == sessionId && action.Status == PendingActionStatus.Pending).ToListAsync(cancellationToken))
            {
                previous.Status = PendingActionStatus.Superseded;
                previous.DecidedBy = DecidedBy.System;
                previous.DecidedAt = now;
            }
        }

        var row = new PendingAction
        {
            Id = PendingActionId.New(),
            OwnerId = context.OwnerId,
            SessionId = request.SessionId,
            SatelliteId = request.SatelliteId,
            Tool = request.Tool.ToString(),
            Args = request.ArgumentsJson,
            Summary = request.Summary,
            Reasons = JsonSerializer.Serialize(request.Reasons, _json),
            Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Language = request.Language,
            CreatedAt = now,
            ExpiresAt = request.ExpiresAt,
        };
        db.PendingActions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    public async Task<PendingActionRecord?> GetPendingAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        PendingAction? row = await db.PendingActions.AsNoTracking()
            .Where(action => action.SessionId == sessionId && action.Status == PendingActionStatus.Pending)
            .OrderByDescending(action => action.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<Result> DecideAsync(PendingActionId id, PendingActionStatus status, DecidedBy decidedBy, CancellationToken cancellationToken)
    {
        PendingAction? row = await db.PendingActions.SingleOrDefaultAsync(action => action.Id == id, cancellationToken);
        if (row is null)
            return new Error("pending_action.not_found", "The pending action does not exist.", ErrorType.NotFound);
        if (row.Status != PendingActionStatus.Pending)
            return new Error("pending_action.not_pending", "The pending action was already decided.", ErrorType.Conflict);

        row.Status = status;
        row.DecidedBy = decidedBy;
        row.DecidedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task DropForSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (PendingAction row in await db.PendingActions.Where(action => action.SessionId == sessionId && action.Status == PendingActionStatus.Pending).ToListAsync(cancellationToken))
        {
            row.Status = PendingActionStatus.Dropped;
            row.DecidedBy = DecidedBy.System;
            row.DecidedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static PendingActionRecord ToRecord(PendingAction row) => new()
    {
        Id = row.Id,
        OwnerId = row.OwnerId,
        SessionId = row.SessionId,
        SatelliteId = row.SatelliteId,
        Tool = ToolId.Parse(row.Tool).Value,
        ArgumentsJson = row.Args,
        Summary = row.Summary,
        Reasons = JsonSerializer.Deserialize<List<ConfirmationReason>>(row.Reasons, _json) ?? [],
        Nonce = row.Nonce,
        Language = row.Language,
        CreatedAt = row.CreatedAt,
        ExpiresAt = row.ExpiresAt,
        Status = row.Status,
    };
}
