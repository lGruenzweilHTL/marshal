using Marshal.Abstractions.Runs;

namespace Marshal.Abstractions.Evals;

/// <summary>
/// Ratings of the current owner's runs. Rating a run while the owner's "help Marshal learn" preference is on also
/// creates its eval case, in the same transaction.
/// </summary>
public interface IRunRatings
{
    /// <summary>Longest rating note.</summary>
    const int MaxNoteLength = 1000;

    /// <summary>Sets or replaces the rating; the created eval case, if any, is in the result.</summary>
    Task<Result<RatedRun>> RateAsync(RunId run, RunRating rating, string? note, CancellationToken cancellationToken);

    /// <summary>Removes the rating; an eval case made from it stays.</summary>
    Task<Result> ClearAsync(RunId run, CancellationToken cancellationToken);
}

/// <param name="Run">The rated run.</param>
/// <param name="Rating">Its rating.</param>
/// <param name="Note">The note, if any.</param>
/// <param name="CaseId">The eval case the rating created; null when none was created.</param>
public sealed record RatedRun(RunId Run, RunRating Rating, string? Note, EvalCaseId? CaseId);
