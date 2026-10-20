using Marshal.Abstractions.Runs;

namespace Marshal.Abstractions.Evals;

/// <summary>The current owner's eval cases. Cases outlive the retention purge of their runs.</summary>
public interface IEvalCaseStore
{
    /// <summary>Snapshots a run as a case (admin "save as case"); the expectation follows the run's rating.</summary>
    Task<Result<EvalCase>> SaveRunAsync(RunId run, CancellationToken cancellationToken);

    /// <summary>Cases, newest first.</summary>
    Task<IReadOnlyList<EvalCase>> ListAsync(CancellationToken cancellationToken);

    Task<EvalCase?> GetAsync(EvalCaseId id, CancellationToken cancellationToken);

    /// <summary>The case made from <paramref name="run"/>, if any.</summary>
    Task<EvalCaseId?> FindByRunAsync(RunId run, CancellationToken cancellationToken);

    Task<Result> UpdateNoteAsync(EvalCaseId id, string? note, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(EvalCaseId id, CancellationToken cancellationToken);
}
