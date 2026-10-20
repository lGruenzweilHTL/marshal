using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;

namespace Marshal.Core.Evals;

/// <summary>A replay's recorder: keeps steps in memory, so nothing reaches the runs tables, and sums the model cost.</summary>
internal sealed class ReplayRunRecorder : IRunRecorder
{
    private readonly Lock _lock = new();
    private int _sequence;

    public decimal Cost { get; private set; }

    public RunOutcome? Outcome { get; private set; }

    public string? Reason { get; private set; }

    public Task<RunId> StartRunAsync(RunStart start, CancellationToken cancellationToken) => Task.FromResult(RunId.New());

    public Task<int> RecordStepAsync(RunId runId, RunStepRecord step, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Cost += step.Cost ?? 0;
            return Task.FromResult(++_sequence);
        }
    }

    public Task CompleteRunAsync(RunId runId, RunCompletion completion, CancellationToken cancellationToken)
    {
        Outcome = completion.Outcome;
        Reason = completion.Reason;
        return Task.CompletedTask;
    }
}

/// <summary>A replay's pending actions: a confirm-tier call becomes a confirmation question that nobody answers.</summary>
internal sealed class ReplayPendingActions(OwnerId owner, TimeProvider time) : IPendingActionStore
{
    public Task<PendingActionRecord> FreezeAsync(FreezeRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PendingActionRecord
        {
            Id = PendingActionId.New(),
            OwnerId = owner,
            SessionId = request.SessionId,
            Tool = request.Tool,
            ArgumentsJson = request.ArgumentsJson,
            Summary = request.Summary,
            Reasons = request.Reasons,
            Nonce = Guid.NewGuid().ToString("N"),
            Language = request.Language,
            CreatedAt = time.GetUtcNow(),
            ExpiresAt = request.ExpiresAt,
        });

    public Task<PendingActionRecord?> GetAsync(PendingActionId id, CancellationToken cancellationToken) => Task.FromResult<PendingActionRecord?>(null);

    public Task<PendingActionRecord?> GetPendingAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.FromResult<PendingActionRecord?>(null);

    public Task<IReadOnlyList<PendingActionRecord>> ListQueuedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PendingActionRecord>>([]);

    public Task<Result> DecideAsync(PendingActionId id, PendingActionStatus status, DecidedBy decidedBy, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());

    public Task DropForSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A clock that stands at the recorded time of the case, so time-dependent rules behave as they did.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
