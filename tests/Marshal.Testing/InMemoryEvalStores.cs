using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Runs;

namespace Marshal.Testing;

/// <summary>Eval cases in memory (single owner).</summary>
public sealed class InMemoryEvalCaseStore : IEvalCaseStore
{
    public ConcurrentDictionary<EvalCaseId, EvalCase> Cases { get; } = new();

    public EvalCase Add(EvalCase evalCase)
    {
        Cases[evalCase.Id] = evalCase;
        return evalCase;
    }

    public Task<Result<EvalCase>> SaveRunAsync(RunId run, CancellationToken cancellationToken) =>
        Task.FromResult<Result<EvalCase>>(Cases.Values.FirstOrDefault(evalCase => evalCase.SourceRunId == run) is { } found ? found : new Error("run.not_found", "No run.", ErrorType.NotFound));

    public Task<IReadOnlyList<EvalCase>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EvalCase>>([.. Cases.Values]);

    public Task<EvalCase?> GetAsync(EvalCaseId id, CancellationToken cancellationToken) => Task.FromResult(Cases.GetValueOrDefault(id));

    public Task<EvalCaseId?> FindByRunAsync(RunId run, CancellationToken cancellationToken) =>
        Task.FromResult(Cases.Values.FirstOrDefault(evalCase => evalCase.SourceRunId == run)?.Id);

    public Task<Result> UpdateNoteAsync(EvalCaseId id, string? note, CancellationToken cancellationToken)
    {
        if (!Cases.TryGetValue(id, out EvalCase? found))
            return Task.FromResult<Result>(new Error("eval_case.not_found", "No case.", ErrorType.NotFound));

        Cases[id] = found with { Note = note };
        return Task.FromResult(Result.Success());
    }

    public Task<Result> DeleteAsync(EvalCaseId id, CancellationToken cancellationToken) =>
        Task.FromResult(Cases.TryRemove(id, out _) ? Result.Success() : new Error("eval_case.not_found", "No case.", ErrorType.NotFound));
}

/// <summary>Eval runs in memory (single owner).</summary>
public sealed class InMemoryEvalRunStore : IEvalRunStore
{
    private readonly ConcurrentDictionary<EvalRunId, EvalRunRecord> _runs = new();
    private readonly ConcurrentDictionary<EvalRunId, List<EvalCaseResult>> _results = new();

    public Task<EvalRunRecord> CreateAsync(CandidateConfig candidate, IReadOnlyList<EvalCaseId> cases, string startedBy, decimal estimatedCost, CancellationToken cancellationToken)
    {
        var run = new EvalRunRecord(EvalRunId.New(), candidate, EvalRunStatus.Queued, cases, startedBy, estimatedCost, DateTimeOffset.UnixEpoch);
        _runs[run.Id] = run;
        _results[run.Id] = [];
        return Task.FromResult(run);
    }

    public Task<IReadOnlyList<EvalRunRecord>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EvalRunRecord>>([.. _runs.Values]);

    public Task<EvalRunRecord?> GetAsync(EvalRunId id, CancellationToken cancellationToken) => Task.FromResult(_runs.GetValueOrDefault(id));

    public Task<IReadOnlyList<EvalCaseResult>> ResultsAsync(EvalRunId id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EvalCaseResult>>([.. _results.GetValueOrDefault(id) ?? []]);

    public Task AddResultAsync(EvalRunId id, EvalCaseResult result, CancellationToken cancellationToken)
    {
        _results[id].Add(result);
        EvalRunRecord run = _runs[id];
        _runs[id] = run with
        {
            Pass = run.Pass + (result.Outcome == EvalOutcome.Pass ? 1 : 0),
            Changed = run.Changed + (result.Outcome == EvalOutcome.Changed ? 1 : 0),
            Diverged = run.Diverged + (result.Outcome == EvalOutcome.Diverged ? 1 : 0),
            Failed = run.Failed + (result.Outcome == EvalOutcome.Failed ? 1 : 0),
            Cost = run.Cost + result.Cost,
        };
        return Task.CompletedTask;
    }

    public Task<bool> SetStatusAsync(EvalRunId id, EvalRunStatus status, string? error, CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(id, out EvalRunRecord? run) || run.Status is EvalRunStatus.Completed or EvalRunStatus.Cancelled or EvalRunStatus.Failed)
            return Task.FromResult(false);

        _runs[id] = run with { Status = status, Error = error };
        return Task.FromResult(true);
    }

    public Task<Result> DeleteAsync(EvalRunId id, CancellationToken cancellationToken) =>
        Task.FromResult(_runs.TryRemove(id, out _) ? Result.Success() : new Error("eval_run.not_found", "No run.", ErrorType.NotFound));
}
