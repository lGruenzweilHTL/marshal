namespace Marshal.Abstractions.Runs;

/// <summary>
/// Records runs and their steps in the domain tables and emits OpenTelemetry from the same data.
/// </summary>
public interface IRunRecorder
{
    /// <summary>Starts a run for the context's owner.</summary>
    Task<RunId> StartRunAsync(RunStart start, CancellationToken cancellationToken);

    /// <summary>Appends a step and returns its sequence number.</summary>
    Task<int> RecordStepAsync(RunId runId, RunStepRecord step, CancellationToken cancellationToken);

    /// <summary>Ends the run; cost is the sum of its steps.</summary>
    Task CompleteRunAsync(RunId runId, RunCompletion completion, CancellationToken cancellationToken);
}
