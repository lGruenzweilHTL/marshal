using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Runs;
using Marshal.Data.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Evals;

/// <summary>
/// Ratings on <c>runs</c>. With the owner's "help Marshal learn" preference on, the first rating of a run also
/// snapshots it into <c>eval_cases</c> in the same save; a later rating updates that case's expectation and note.
/// </summary>
public sealed class EfRunRatings(IDbContextFactory<MarshalDbContext> contexts, TimeProvider time) : IRunRatings
{
    public async Task<Result<RatedRun>> RateAsync(RunId run, RunRating rating, string? note, CancellationToken cancellationToken)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > IRunRatings.MaxNoteLength })
            return new Error("rating.note_too_long", $"The note can have at most {IRunRatings.MaxNoteLength} characters.", ErrorType.Validation);

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Run? row = await db.Runs.SingleOrDefaultAsync(candidate => candidate.Id == run, cancellationToken);
        if (row is null)
            return NotFound();

        DateTimeOffset now = time.GetUtcNow();
        row.Rating = rating;
        row.RatingNote = note;
        row.RatedAt = now;

        EvalCaseId? caseId = null;
        EvalCaseRow? existing = await db.EvalCases.SingleOrDefaultAsync(candidate => candidate.SourceRunId == run, cancellationToken);
        if (existing is not null)
        {
            existing.Expectation = EvalCaseSnapshots.ExpectationOf(rating);
            existing.Note = note;
        }
        else if (await LearningOnAsync(db, row, cancellationToken))
        {
            EvalCaseRow created = await EvalCaseSnapshots.CreateAsync(db, row, now, cancellationToken);
            db.EvalCases.Add(created);
            caseId = created.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RatedRun(run, rating, note, caseId);
    }

    public async Task<Result> ClearAsync(RunId run, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Run? row = await db.Runs.SingleOrDefaultAsync(candidate => candidate.Id == run, cancellationToken);
        if (row is null)
            return NotFound();

        row.Rating = null;
        row.RatingNote = null;
        row.RatedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Task<bool> LearningOnAsync(MarshalDbContext db, Run run, CancellationToken cancellationToken) =>
        db.UserPreferences.AsNoTracking().AnyAsync(preferences => preferences.UserId == run.OwnerId && preferences.HelpMarshalLearn, cancellationToken);

    private static Error NotFound() => new("run.not_found", "The run does not exist.", ErrorType.NotFound);
}
