namespace Marshal.Abstractions.Runs;

/// <summary>
/// Receives the same run data the recorder stores (OpenTelemetry export).
/// </summary>
public interface IRunObserver
{
    void RunStarted(RunId runId, RunStart start, DateTimeOffset startedAt);

    void StepRecorded(RunId runId, int sequence, RunStepRecord step, DateTimeOffset recordedAt);

    void RunCompleted(RunId runId, RunCompletion completion, decimal cost, DateTimeOffset endedAt);
}
