using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Conversations;

/// <summary>
/// Sessions of the current owner.
/// </summary>
public sealed class EfSessionStore(MarshalDbContext db, IExecutionContextAccessor executionContext, TimeProvider timeProvider) : ISessionStore
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<SessionRecord?> FindOpenByTokenAsync(ApiTokenId apiTokenId, string externalId, CancellationToken cancellationToken)
    {
        Session? row = await db.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(session => session.ApiTokenId == apiTokenId && session.ExternalId == externalId && session.ClosedAt == null, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<SessionRecord?> FindOpenAsync(SessionId id, CancellationToken cancellationToken)
    {
        Session? row = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(session => session.Id == id && session.ClosedAt == null, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<SessionRecord> CreateAsync(NewSession session, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = executionContext.Current
            ?? throw new InvalidOperationException("Creating a session requires an execution context.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        var row = new Session
        {
            Id = SessionId.New(),
            OwnerId = context.OwnerId,
            Source = session.Source,
            ApiTokenId = session.ApiTokenId,
            ExternalId = session.ExternalId,
            IdentityEvidence = JsonSerializer.Serialize(session.Evidence, _json),
            ResolvedBy = session.ResolvedBy,
            CreatedAt = now,
            LastTurnAt = now,
        };
        db.Sessions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    public async Task TouchAsync(SessionId id, CancellationToken cancellationToken)
    {
        Session? row = await db.Sessions.SingleOrDefaultAsync(session => session.Id == id, cancellationToken);
        if (row is null)
            return;

        row.LastTurnAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CloseAsync(SessionId id, CancellationToken cancellationToken)
    {
        Session? row = await db.Sessions.SingleOrDefaultAsync(session => session.Id == id && session.ClosedAt == null, cancellationToken);
        if (row is null)
            return;

        row.ClosedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionId>> FindIdleAsync(DateTimeOffset idleBefore, CancellationToken cancellationToken) =>
        await db.Sessions.AsNoTracking()
            .Where(session => session.ClosedAt == null && session.LastTurnAt < idleBefore)
            .Select(session => session.Id)
            .ToListAsync(cancellationToken);

    private static SessionRecord ToRecord(Session row) =>
        new(row.Id, row.OwnerId, row.Source, row.ApiTokenId, row.ExternalId, row.ResolvedBy, row.LastTurnAt);
}
