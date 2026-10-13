using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Conversations;

/// <summary>
/// Conversations of the current owner from the session, run and task tables.
/// </summary>
public sealed class EfConversationReader(IDbContextFactory<MarshalDbContext> contexts, IToolCallSummarizer summarizer) : IConversationReader
{
    private const int MaxLimit = 200;
    private const int TitleLength = 80;

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(DateTimeOffset? before, int limit, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<Session> query = db.Sessions.AsNoTracking().Where(session => session.Source != SessionSource.Task);
        if (before is { } cursor)
            query = query.Where(session => session.LastTurnAt < cursor);

        List<Session> sessions = await query.OrderByDescending(session => session.LastTurnAt).Take(Math.Clamp(limit, 1, MaxLimit)).ToListAsync(cancellationToken);
        List<SessionId?> ids = [.. sessions.Select(session => (SessionId?)session.Id)];
        var runs = await db.Runs.AsNoTracking()
            .Where(run => ids.Contains(run.SessionId) && ConversationEntries.TurnTriggers.Contains(run.Trigger))
            .Select(run => new { run.Id, run.SessionId, run.StartedAt, run.SatelliteId, run.AreaId })
            .ToListAsync(cancellationToken);
        var turns = runs.GroupBy(run => run.SessionId!.Value).ToDictionary(group => group.Key, group => group.OrderBy(run => run.StartedAt).ToList());
        Dictionary<RunId, string?> inputs = await ConversationEntries.InputsAsync(db, [.. turns.Values.Select(group => group[0].Id)], cancellationToken);

        return [.. sessions.Select(session =>
        {
            var own = turns.GetValueOrDefault(session.Id);
            var first = own?[0];
            return Summary(session, own?.Count ?? 0, first is null ? null : inputs.GetValueOrDefault(first.Id), first?.SatelliteId, first?.AreaId);
        })];
    }

    public async Task<ConversationDetail?> GetAsync(SessionId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Session? session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.Source != SessionSource.Task, cancellationToken);
        if (session is null)
            return null;

        List<ConversationEntry> entries = await ConversationEntries.LoadAsync(db, id, after: null, maxTurns: null, cancellationToken, details: summarizer);
        List<TurnEntry> turns = [.. entries.OfType<TurnEntry>()];
        TurnEntry? first = turns.FirstOrDefault();
        return new ConversationDetail(Summary(session, turns.Count, first?.Input, first?.SatelliteId, first?.AreaId), entries);
    }

    private static ConversationSummary Summary(Session session, int turns, string? firstInput, string? satelliteId, string? areaId) =>
        new(
            session.Id,
            session.Source,
            session.CreatedAt,
            session.LastTurnAt,
            turns,
            Title(firstInput),
            session.Source == SessionSource.Web && session.ClosedAt is null,
            satelliteId,
            areaId);

    private static string? Title(string? input)
    {
        string? trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length <= TitleLength ? trimmed : trimmed[..TitleLength];
    }
}
