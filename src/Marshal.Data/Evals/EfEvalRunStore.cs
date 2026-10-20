using System.Text.Json;
using System.Text.Json.Serialization;
using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Execution;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Evals;

/// <summary>Eval runs in <c>eval_runs</c> and <c>eval_results</c>, scoped to the current owner.</summary>
public sealed class EfEvalRunStore(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext, TimeProvider time) : IEvalRunStore
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public async Task<EvalRunRecord> CreateAsync(CandidateConfig candidate, IReadOnlyList<EvalCaseId> cases, string startedBy, decimal estimatedCost, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = new EvalRunRow
        {
            Id = EvalRunId.New(),
            OwnerId = executionContext.Current?.OwnerId ?? throw new InvalidOperationException("An eval run needs an owner."),
            Candidate = JsonSerializer.Serialize(candidate, _json),
            CaseIds = JsonSerializer.Serialize(cases.Select(id => id.Value), _json),
            StartedBy = startedBy,
            Status = EvalRunStatus.Queued,
            EstimatedCost = estimatedCost,
            CreatedAt = time.GetUtcNow(),
        };
        db.EvalRuns.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    public async Task<IReadOnlyList<EvalRunRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<EvalRunRow> rows = await db.EvalRuns.AsNoTracking().OrderByDescending(row => row.CreatedAt).ToListAsync(cancellationToken);
        return [.. rows.Select(ToRecord)];
    }

    public async Task<EvalRunRecord?> GetAsync(EvalRunId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        EvalRunRow? row = await db.EvalRuns.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<EvalCaseResult>> ResultsAsync(EvalRunId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<EvalResultRow> rows = await db.EvalResults.AsNoTracking().Where(row => row.RunId == id).OrderBy(row => row.CreatedAt).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new EvalCaseResult(
            row.CaseId, row.Outcome, JsonSerializer.Deserialize<List<ReplayedCall>>(row.Calls, _json) ?? [], row.Reply, row.JudgeScore, row.Cost, row.Error))];
    }

    public async Task AddResultAsync(EvalRunId id, EvalCaseResult result, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        EvalRunRow run = await db.EvalRuns.SingleAsync(candidate => candidate.Id == id, cancellationToken);
        db.EvalResults.Add(new EvalResultRow
        {
            Id = Guid.CreateVersion7(),
            OwnerId = run.OwnerId,
            RunId = id,
            CaseId = result.CaseId,
            Outcome = result.Outcome,
            Calls = JsonSerializer.Serialize(result.Calls, _json),
            Reply = result.Reply,
            JudgeScore = result.JudgeScore,
            Cost = result.Cost,
            Error = result.Error,
            CreatedAt = time.GetUtcNow(),
        });
        switch (result.Outcome)
        {
            case EvalOutcome.Pass:
                run.Pass++;
                break;
            case EvalOutcome.Changed:
                run.Changed++;
                break;
            case EvalOutcome.Diverged:
                run.Diverged++;
                break;
            default:
                run.Failed++;
                break;
        }

        run.Cost += result.Cost;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SetStatusAsync(EvalRunId id, EvalRunStatus status, string? error, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        EvalRunRow? run = await db.EvalRuns.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (run is null || run.Status is EvalRunStatus.Completed or EvalRunStatus.Cancelled or EvalRunStatus.Failed)
            return false;

        run.Status = status;
        run.Error = error;
        if (status is EvalRunStatus.Completed or EvalRunStatus.Cancelled or EvalRunStatus.Failed)
            run.EndedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result> DeleteAsync(EvalRunId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        EvalRunRow? run = await db.EvalRuns.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (run is null)
            return new Error("eval_run.not_found", "The eval run does not exist.", ErrorType.NotFound);
        if (run.Status is EvalRunStatus.Queued or EvalRunStatus.Running)
            return new Error("eval_run.active", "Cancel the eval run before deleting it.", ErrorType.Conflict);

        await db.EvalResults.Where(result => result.RunId == id).ExecuteDeleteAsync(cancellationToken);
        db.EvalRuns.Remove(run);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static EvalRunRecord ToRecord(EvalRunRow row) =>
        new(
            row.Id,
            JsonSerializer.Deserialize<CandidateConfig>(row.Candidate, _json)!,
            row.Status,
            [.. (JsonSerializer.Deserialize<List<Guid>>(row.CaseIds, _json) ?? []).Select(EvalCaseId.From)],
            row.StartedBy,
            row.EstimatedCost,
            row.CreatedAt)
        {
            Pass = row.Pass,
            Changed = row.Changed,
            Diverged = row.Diverged,
            Failed = row.Failed,
            Cost = row.Cost,
            EndedAt = row.EndedAt,
            Error = row.Error,
        };
}
