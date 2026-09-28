using Marshal.Abstractions.Runs;

namespace Marshal.Testing;

/// <summary>
/// Run recorder that keeps everything in memory for assertions.
/// </summary>
public sealed class InMemoryRunRecorder : IRunRecorder
{
    private readonly Lock _lock = new();

    public List<RecordedRun> Runs { get; } = [];

    public RecordedRun OnlyRun => Runs.Count == 1 ? Runs[0] : throw new InvalidOperationException($"Expected one run, found {Runs.Count}.");

    public Task<RunId> StartRunAsync(RunStart start, CancellationToken cancellationToken)
    {
        var run = new RecordedRun(RunId.New(), start);
        lock (_lock)
        {
            Runs.Add(run);
        }

        return Task.FromResult(run.Id);
    }

    public Task<int> RecordStepAsync(RunId runId, RunStepRecord step, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            RecordedRun run = Runs.Single(candidate => candidate.Id == runId);
            run.Steps.Add(step);
            return Task.FromResult(run.Steps.Count - 1);
        }
    }

    public Task CompleteRunAsync(RunId runId, RunCompletion completion, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Runs.Single(candidate => candidate.Id == runId).Completion = completion;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// A run recorded by <see cref="InMemoryRunRecorder"/>.
/// </summary>
public sealed class RecordedRun(RunId id, RunStart start)
{
    public RunId Id { get; } = id;

    public RunStart Start { get; } = start;

    public List<RunStepRecord> Steps { get; } = [];

    public RunCompletion? Completion { get; set; }

    public IReadOnlyList<RunStepKind> Kinds => [.. Steps.Select(step => step.Kind)];
}
