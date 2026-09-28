using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;

namespace Marshal.Testing;

/// <summary>
/// Session store honoring the current execution context's owner, like the EF store.
/// </summary>
public sealed class InMemorySessionStore(IExecutionContextAccessor executionContext, TimeProvider timeProvider) : ISessionStore
{
    private readonly List<(SessionRecord Record, DateTimeOffset? ClosedAt, Abstractions.Identity.IdentityEvidence Evidence)> _sessions = [];

    public IReadOnlyList<SessionRecord> All => [.. _sessions.Select(entry => entry.Record)];

    public IReadOnlyList<SessionId> Closed => [.. _sessions.Where(entry => entry.ClosedAt is not null).Select(entry => entry.Record.Id)];

    public Abstractions.Identity.IdentityEvidence EvidenceOf(SessionId id) => _sessions.Single(entry => entry.Record.Id == id).Evidence;

    public Task<SessionRecord?> FindOpenByTokenAsync(ApiTokenId apiTokenId, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Open().FirstOrDefault(record => record.ApiTokenId == apiTokenId && record.ExternalId == externalId));

    public Task<SessionRecord?> FindOpenAsync(SessionId id, CancellationToken cancellationToken) =>
        Task.FromResult(Open().FirstOrDefault(record => record.Id == id));

    public Task<SessionRecord> CreateAsync(NewSession session, CancellationToken cancellationToken)
    {
        var record = new SessionRecord(SessionId.New(), Owner(), session.Source, session.ApiTokenId, session.ExternalId, session.ResolvedBy, timeProvider.GetUtcNow());
        _sessions.Add((record, null, session.Evidence));
        return Task.FromResult(record);
    }

    public Task TouchAsync(SessionId id, CancellationToken cancellationToken)
    {
        int index = _sessions.FindIndex(entry => entry.Record.Id == id && entry.Record.OwnerId == Owner());
        if (index >= 0)
            _sessions[index] = _sessions[index] with { Record = _sessions[index].Record with { LastTurnAt = timeProvider.GetUtcNow() } };

        return Task.CompletedTask;
    }

    public Task CloseAsync(SessionId id, CancellationToken cancellationToken)
    {
        int index = _sessions.FindIndex(entry => entry.Record.Id == id && entry.Record.OwnerId == Owner());
        if (index >= 0)
            _sessions[index] = _sessions[index] with { ClosedAt = timeProvider.GetUtcNow() };

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionId>> FindIdleAsync(DateTimeOffset idleBefore, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SessionId>>([.. Open().Where(record => record.LastTurnAt < idleBefore).Select(record => record.Id)]);

    private IEnumerable<SessionRecord> Open() =>
        _sessions.Where(entry => entry.ClosedAt is null && entry.Record.OwnerId == Owner()).Select(entry => entry.Record);

    private Abstractions.Identity.OwnerId Owner() =>
        executionContext.Current?.OwnerId ?? throw new InvalidOperationException("No execution context.");
}
