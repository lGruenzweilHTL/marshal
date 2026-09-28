using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Conversations;

/// <summary>
/// Runs, steps and cost of the current owner from the run tables.
/// </summary>
public sealed class EfRunQueries(IDbContextFactory<MarshalDbContext> contexts) : IRunQueries
{
    private const string AdvisorTier = "advisor";
    private const int MaxLimit = 200;

    public async Task<IReadOnlyList<RunSummary>> ListAsync(RunFilter filter, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<Run> runs = db.Runs.AsNoTracking();
        if (filter.Source is { } source)
            runs = runs.Where(run => run.Source == source);
        if (filter.Outcome is { } outcome)
            runs = runs.Where(run => run.Outcome == outcome);
        if (filter.MinCost is { } minCost)
            runs = runs.Where(run => run.Cost >= minCost);
        if (filter.Before is { } before)
            runs = runs.Where(run => run.StartedAt < before);

        List<Run> rows = await runs.OrderByDescending(run => run.StartedAt).Take(Math.Clamp(filter.Limit, 1, MaxLimit)).ToListAsync(cancellationToken);
        return [.. rows.Select(Summary)];
    }

    public async Task<RunDetail?> GetAsync(RunId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Run? run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (run is null)
            return null;

        List<RunStep> steps = await db.RunSteps.AsNoTracking().Where(step => step.RunId == id).OrderBy(step => step.Sequence).ToListAsync(cancellationToken);
        IReadOnlyList<TaintSource> taint = run.TaintSources is null ? [] : JsonSerializer.Deserialize<List<TaintSource>>(run.TaintSources, DbRunRecorder.StepPayload.Json) ?? [];
        return new RunDetail(Summary(run), run.Language, taint, [.. steps.Select(Step)]);
    }

    public async Task<CostReport> CostAsync(DateOnly from, DateOnly to, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        DateTimeOffset start = LocalMidnight(from, timeZone);
        DateTimeOffset end = LocalMidnight(to.AddDays(1), timeZone);
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        // Same basis as the advisor budget: model call costs, attributed to the day their run started.
        var calls = await db.RunSteps.AsNoTracking()
            .Where(step => step.Kind == RunStepKind.ModelCall && step.Cost != null)
            .Join(db.Runs.Where(run => run.StartedAt >= start && run.StartedAt < end), step => step.RunId, run => run.Id, (step, run) => new { run.StartedAt, step.Tier, step.Cost })
            .ToListAsync(cancellationToken);

        List<DailyCost> days = [.. calls
            .GroupBy(call => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(call.StartedAt, timeZone).DateTime))
            .OrderBy(day => day.Key)
            .Select(day => Daily(day.Key, [.. day.Select(call => (call.Tier ?? "unknown", call.Cost!.Value))]))];
        return new CostReport(
            timeZone.Id,
            from,
            to,
            days,
            days.Sum(day => day.Total),
            Merge(days.Select(day => day.ByTier)),
            days.Sum(day => day.Advisor));
    }

    private static DailyCost Daily(DateOnly date, List<(string Tier, decimal Cost)> calls)
    {
        var byTier = calls.Where(call => call.Tier != AdvisorTier)
            .GroupBy(call => call.Tier, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(call => call.Cost), StringComparer.Ordinal);
        return new DailyCost(date, calls.Sum(call => call.Cost), byTier, calls.Where(call => call.Tier == AdvisorTier).Sum(call => call.Cost));
    }

    private static Dictionary<string, decimal> Merge(IEnumerable<IReadOnlyDictionary<string, decimal>> days)
    {
        var merged = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach ((string tier, decimal cost) in days.SelectMany(day => day))
            merged[tier] = merged.GetValueOrDefault(tier) + cost;

        return merged;
    }

    private static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private static RunSummary Summary(Run run) => new(
        run.Id, run.SessionId, run.Source, run.InputMode, run.Trigger, run.SatelliteId, run.StartedAt, run.EndedAt, run.Outcome, run.OutcomeReason, run.Tainted, run.Cost);

    private static RunStepDetail Step(RunStep step)
    {
        DbRunRecorder.StepPayload payload = step.Payload is null
            ? DbRunRecorder.StepPayload.Empty
            : JsonSerializer.Deserialize<DbRunRecorder.StepPayload>(step.Payload, DbRunRecorder.StepPayload.Json) ?? DbRunRecorder.StepPayload.Empty;
        return new RunStepDetail(
            step.Sequence, step.Kind, step.Model, step.Tier, step.TokensIn, step.TokensOut, step.TokensCached, step.FirstTokenMs, step.DurationMs, step.Cost,
            step.Tool, step.RiskTier, step.GuardDecision, payload.Reasons ?? [], payload.Input, payload.Content, payload.Arguments, payload.Reference,
            payload.ContinueConversation, step.Error, step.Depth);
    }
}
