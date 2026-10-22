using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Suggestions;
using Marshal.Data;
using Marshal.Data.Suggestions;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.Suggestions;

/// <summary>Suggestions and detector counts in the database, scoped to the current owner by the query filter.</summary>
public sealed class EfSuggestionStore(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext) : ISuggestionStore
{
    public async Task<SuggestionDetectorStats> GetStatsAsync(string detector, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        SuggestionDetectorStat? row = await db.SuggestionDetectorStats.AsNoTracking().FirstOrDefaultAsync(row => row.Detector == detector, cancellationToken);
        return row is null ? new SuggestionDetectorStats(detector, 0, 0, 0) : ToStats(row);
    }

    public async Task<IReadOnlyList<SuggestionDetectorStats>> ListStatsAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<SuggestionDetectorStat> rows = await db.SuggestionDetectorStats.AsNoTracking().OrderBy(row => row.Detector).ToListAsync(cancellationToken);
        return [.. rows.Select(ToStats)];
    }

    public async Task<bool> SentSinceAsync(string detector, string dedupeKey, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Suggestions.AnyAsync(row => row.Detector == detector && row.DedupeKey == dedupeKey && row.CreatedAt >= since, cancellationToken);
    }

    public async Task AddAsync(SuggestionRecord suggestion, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        db.Suggestions.Add(new Suggestion
        {
            Id = suggestion.Id,
            OwnerId = suggestion.OwnerId,
            Detector = suggestion.Detector,
            DedupeKey = suggestion.DedupeKey,
            Text = suggestion.Text,
            FactsJson = JsonSerializer.Serialize(suggestion.Facts),
            Delivery = suggestion.Delivery,
            Status = suggestion.Status,
            Nonce = suggestion.Nonce,
            PendingActionId = suggestion.PendingActionId,
            ActionSummary = suggestion.ActionSummary,
            Snippet = suggestion.Snippet,
            CreatedAt = suggestion.CreatedAt,
            ExpiresAt = suggestion.ExpiresAt,
        });
        SuggestionDetectorStat stat = await StatAsync(db, suggestion.Detector, cancellationToken);
        stat.Sent++;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetNotificationAsync(Guid id, Guid notificationId, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Suggestions.Where(row => row.Id == id).ExecuteUpdateAsync(update => update.SetProperty(row => row.NotificationId, notificationId), cancellationToken);
    }

    public async Task<SuggestionRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Suggestion? row = await db.Suggestions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<SuggestionRecord>> ListOpenAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<Suggestion> rows = await db.Suggestions.AsNoTracking().Where(row => row.Status == SuggestionStatus.Open).ToListAsync(cancellationToken);
        return [.. rows.OrderByDescending(row => row.CreatedAt).Select(ToRecord)];
    }

    public async Task<Result<SuggestionRecord>> DecideAsync(Guid id, SuggestionStatus status, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Suggestion? row = await db.Suggestions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null)
            return SuggestionErrors.NotFound();
        if (row.Status != SuggestionStatus.Open)
            return SuggestionErrors.AlreadyDecided();

        row.Status = status;
        row.DecidedAt = now;
        if (status is SuggestionStatus.Accepted or SuggestionStatus.Dismissed)
        {
            SuggestionDetectorStat stat = await StatAsync(db, row.Detector, cancellationToken);
            if (status == SuggestionStatus.Accepted)
                stat.Accepted++;
            else
                stat.Dismissed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    public async Task MuteAsync(string detector, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        SuggestionDetectorStat stat = await StatAsync(db, detector, cancellationToken);
        stat.MutedUntil = until;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAsync(string detector, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        SuggestionDetectorStat stat = await StatAsync(db, detector, cancellationToken);
        (stat.Sent, stat.Accepted, stat.Dismissed) = (0, 0, 0);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(Guid Id, OwnerId Owner)?> FindByNonceAsync(string nonce, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Suggestions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Nonce == nonce && row.Status == SuggestionStatus.Open)
            .Select(row => new { row.Id, row.OwnerId })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Id, row.OwnerId);
    }

    public async Task<IReadOnlyList<(Guid Id, OwnerId Owner, string Detector, string DedupeKey, DateTimeOffset ExpiresAt)>> ListAllOpenAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Suggestions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Status == SuggestionStatus.Open)
            .Select(row => new { row.Id, row.OwnerId, row.Detector, row.DedupeKey, row.ExpiresAt, row.CreatedAt })
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.CreatedAt).Select(row => (row.Id, row.OwnerId, row.Detector, row.DedupeKey, row.ExpiresAt))];
    }

    private async Task<SuggestionDetectorStat> StatAsync(MarshalDbContext db, string detector, CancellationToken cancellationToken)
    {
        SuggestionDetectorStat? stat = await db.SuggestionDetectorStats.FirstOrDefaultAsync(row => row.Detector == detector, cancellationToken);
        if (stat is not null)
            return stat;

        stat = new SuggestionDetectorStat { OwnerId = executionContext.Current!.OwnerId, Detector = detector };
        db.SuggestionDetectorStats.Add(stat);
        return stat;
    }

    private static SuggestionDetectorStats ToStats(SuggestionDetectorStat row) => new(row.Detector, row.Sent, row.Accepted, row.Dismissed, row.MutedUntil);

    private static SuggestionRecord ToRecord(Suggestion row) => new(
        row.Id,
        row.OwnerId,
        row.Detector,
        row.DedupeKey,
        row.Text,
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.FactsJson) ?? [],
        row.Delivery,
        row.Status,
        row.Nonce,
        row.PendingActionId,
        row.ActionSummary,
        row.CreatedAt,
        row.ExpiresAt,
        row.DecidedAt,
        row.Snippet);
}

/// <summary>Errors of suggestion decisions.</summary>
public static class SuggestionErrors
{
    public static Error NotFound() => new("suggestion.not_found", "There is no such suggestion.", ErrorType.NotFound);

    public static Error AlreadyDecided() => new("suggestion.decided", "The suggestion was already answered or has expired.", ErrorType.Conflict);
}
