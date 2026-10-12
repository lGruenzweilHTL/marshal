using Marshal.Abstractions.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Conversations;

/// <summary>
/// The summary watermark in <c>sessions.summarized_until</c>, owner-scoped like every session query.
/// </summary>
public sealed class EfSummaryWatermark(IDbContextFactory<MarshalDbContext> contexts) : ISummaryWatermark
{
    public async Task<DateTimeOffset?> GetAsync(SessionId session, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Sessions.AsNoTracking().Where(row => row.Id == session).Select(row => row.SummarizedUntil).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SetAsync(SessionId session, DateTimeOffset summarizedUntil, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Session? row = await db.Sessions.SingleOrDefaultAsync(candidate => candidate.Id == session, cancellationToken);
        if (row is null || row.SummarizedUntil >= summarizedUntil)
            return;

        row.SummarizedUntil = summarizedUntil;
        await db.SaveChangesAsync(cancellationToken);
    }
}
