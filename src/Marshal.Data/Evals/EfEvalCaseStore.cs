using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Runs;
using Marshal.Data.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Evals;

/// <summary>Eval cases in <c>eval_cases</c>, scoped to the current owner.</summary>
public sealed class EfEvalCaseStore(IDbContextFactory<MarshalDbContext> contexts, TimeProvider time) : IEvalCaseStore
{
    public async Task<Result<EvalCase>> SaveRunAsync(RunId run, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await db.EvalCases.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.SourceRunId == run, cancellationToken) is { } existing)
            return EvalCaseSnapshots.ToCase(existing);

        Run? row = await db.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == run, cancellationToken);
        if (row is null)
            return new Error("run.not_found", "The run does not exist.", ErrorType.NotFound);

        EvalCaseRow created = await EvalCaseSnapshots.CreateAsync(db, row, time.GetUtcNow(), cancellationToken);
        db.EvalCases.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return EvalCaseSnapshots.ToCase(created);
    }

    public async Task<IReadOnlyList<EvalCase>> ListAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<EvalCaseRow> rows = await db.EvalCases.AsNoTracking().OrderByDescending(row => row.CreatedAt).ToListAsync(cancellationToken);
        return [.. rows.Select(EvalCaseSnapshots.ToCase)];
    }

    public async Task<EvalCase?> GetAsync(EvalCaseId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        EvalCaseRow? row = await db.EvalCases.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null ? null : EvalCaseSnapshots.ToCase(row);
    }

    public async Task<EvalCaseId?> FindByRunAsync(RunId run, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.EvalCases.AsNoTracking().Where(candidate => candidate.SourceRunId == run).Select(candidate => (EvalCaseId?)candidate.Id).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Result> UpdateNoteAsync(EvalCaseId id, string? note, CancellationToken cancellationToken)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > IRunRatings.MaxNoteLength })
            return new Error("eval_case.note_too_long", $"The note can have at most {IRunRatings.MaxNoteLength} characters.", ErrorType.Validation);

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        EvalCaseRow? row = await db.EvalCases.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null)
            return NotFound();

        row.Note = note;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(EvalCaseId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        EvalCaseRow? row = await db.EvalCases.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null)
            return NotFound();

        db.EvalCases.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Error NotFound() => new("eval_case.not_found", "The eval case does not exist.", ErrorType.NotFound);
}
