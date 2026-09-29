using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Sessions;

namespace Marshal.Testing;

/// <summary>
/// Pending action store for flow and loop tests (single owner).
/// </summary>
public sealed class InMemoryPendingActionStore(TimeProvider timeProvider) : IPendingActionStore
{
    private readonly List<PendingActionRecord> _actions = [];

    /// <summary>Final status and decider per action id.</summary>
    public Dictionary<PendingActionId, (PendingActionStatus Status, DecidedBy By)> Decisions { get; } = [];

    /// <summary>All frozen actions in order.</summary>
    public IReadOnlyList<PendingActionRecord> Actions => _actions;

    public Task<PendingActionRecord> FreezeAsync(FreezeRequest request, CancellationToken cancellationToken)
    {
        foreach (PendingActionRecord previous in _actions.Where(action => action.SessionId == request.SessionId && IsPending(action)).ToList())
            Decisions[previous.Id] = (PendingActionStatus.Superseded, DecidedBy.System);

        var record = new PendingActionRecord
        {
            Id = PendingActionId.New(),
            OwnerId = TestExecutionContext.OwnerA,
            SessionId = request.SessionId,
            SatelliteId = request.SatelliteId,
            TaskId = request.TaskId,
            RunId = request.RunId,
            Taint = request.Taint ?? [],
            Tool = request.Tool,
            ArgumentsJson = request.ArgumentsJson,
            Summary = request.Summary,
            Reasons = request.Reasons,
            Nonce = Guid.NewGuid().ToString("N"),
            Language = request.Language,
            CreatedAt = timeProvider.GetUtcNow(),
            ExpiresAt = request.ExpiresAt,
        };
        _actions.Add(record);
        return Task.FromResult(record);
    }

    public Task<PendingActionRecord?> GetAsync(PendingActionId id, CancellationToken cancellationToken) =>
        Task.FromResult(_actions.FirstOrDefault(action => action.Id == id) is { } found
            ? found with { Status = Decisions.TryGetValue(id, out (PendingActionStatus Status, DecidedBy By) decided) ? decided.Status : PendingActionStatus.Pending }
            : null);

    public Task<PendingActionRecord?> GetPendingAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(_actions.LastOrDefault(action => action.SessionId == sessionId && IsPending(action)));

    public Task<IReadOnlyList<PendingActionRecord>> ListTaskApprovalsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PendingActionRecord>>([.. _actions.Where(action => action.TaskId is not null && IsPending(action))]);

    public Task<Result> DecideAsync(PendingActionId id, PendingActionStatus status, DecidedBy decidedBy, CancellationToken cancellationToken)
    {
        if (Decisions.ContainsKey(id))
            return Task.FromResult<Result>(new Error("pending_action.not_pending", "already decided", ErrorType.Conflict));

        Decisions[id] = (status, decidedBy);
        return Task.FromResult(Result.Success());
    }

    public Task DropForSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        foreach (PendingActionRecord action in _actions.Where(action => action.SessionId == sessionId && IsPending(action)).ToList())
            Decisions[action.Id] = (PendingActionStatus.Dropped, DecidedBy.System);

        return Task.CompletedTask;
    }

    private bool IsPending(PendingActionRecord action) => !Decisions.ContainsKey(action.Id);
}
