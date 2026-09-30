using Marshal.Abstractions;
using Marshal.Abstractions.Executors;
using Marshal.Data;
using Marshal.Data.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Executors;

/// <summary>
/// <see cref="IProposalReview"/> on the <c>proposals</c> table (scoped to the current owner by the context's query
/// filter). Merging asks the executor that produced the proposal to apply it and only then marks it merged, so a
/// refused merge leaves it open; rejecting marks it first and then asks the executor to drop the change.
/// </summary>
public sealed partial class ProposalReview(
    IEnumerable<IExecutor> executors,
    IDbContextFactory<MarshalDbContext> contexts,
    TimeProvider time,
    ILogger<ProposalReview> logger) : IProposalReview
{
    private const int MaxProposals = 100;

    private readonly IReadOnlyList<IExecutor> _executors = [.. executors];

    public async Task<IReadOnlyList<ProposalRecord>> ListAsync(ProposalStatus? status, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<Proposal> rows = db.Proposals.AsNoTracking();
        if (status is { } wanted)
            rows = rows.Where(row => row.Status == wanted);

        return await rows.OrderByDescending(row => row.CreatedAt)
            .Take(MaxProposals)
            .Select(row => new ProposalRecord(row.Id, row.TaskId, row.ExecutorSessionId, row.Kind, row.Target, row.Branch, row.Title, null, row.Status, row.CreatedAt, row.DecidedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<ProposalRecord>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Proposal? row = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(proposal => proposal.Id == id, cancellationToken);
        return row is null ? NotFound() : Record(row);
    }

    public async Task<Result<ProposalRecord>> MergeAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<(Proposal Proposal, ExecutorSession Session, IExecutor Executor)> source = await SourceAsync(id, cancellationToken);
        if (source.IsFailure)
            return source.Error;

        (Proposal proposal, ExecutorSession session, IExecutor executor) = source.Value;
        Result merged = await executor.MergeAsync(session.Handle, session.Target ?? proposal.Target, cancellationToken);
        if (merged.IsFailure)
        {
            LogMergeRefused(id, merged.Error.Code);
            return merged.Error;
        }

        return await DecideAsync(id, ProposalStatus.Merged, cancellationToken);
    }

    public async Task<Result<ProposalRecord>> RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<(Proposal Proposal, ExecutorSession Session, IExecutor Executor)> source = await SourceAsync(id, cancellationToken);
        if (source.IsFailure && source.Error.Code != "proposal.no_executor")
            return source.Error;

        Result<ProposalRecord> rejected = await DecideAsync(id, ProposalStatus.Rejected, cancellationToken);
        if (rejected.IsFailure || source.IsFailure)
            return rejected;

        (Proposal proposal, ExecutorSession session, IExecutor executor) = source.Value;
        Result discarded = await executor.DiscardAsync(session.Handle, session.Target ?? proposal.Target, CancellationToken.None);
        if (discarded.IsFailure)
            LogDiscardFailed(id, discarded.Error.Message);
        return rejected;
    }

    // The open proposal with its session and executor; proposal.no_executor when either is gone.
    private async Task<Result<(Proposal, ExecutorSession, IExecutor)>> SourceAsync(Guid id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Proposal? proposal = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (proposal is null)
            return NotFound();
        if (proposal.Status != ProposalStatus.Open)
            return Decided();

        ExecutorSession? session = proposal.ExecutorSessionId is { } sessionId
            ? await db.ExecutorSessions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == sessionId, cancellationToken)
            : null;
        IExecutor? executor = session is null ? null : _executors.FirstOrDefault(candidate => string.Equals(candidate.Name, session.Executor, StringComparison.OrdinalIgnoreCase));
        if (session is null || executor is null)
            return new Error("proposal.no_executor", "The executor that produced this proposal is not configured any more.", ErrorType.Unavailable);
        return (proposal, session, executor);
    }

    private async Task<Result<ProposalRecord>> DecideAsync(Guid id, ProposalStatus status, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        int changed = await db.Proposals
            .Where(row => row.Id == id && row.Status == ProposalStatus.Open)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, status).SetProperty(row => row.DecidedAt, now), CancellationToken.None);
        if (changed == 0)
            return Decided();

        return Record(await db.Proposals.AsNoTracking().SingleAsync(row => row.Id == id, CancellationToken.None));
    }

    private static ProposalRecord Record(Proposal row) =>
        new(row.Id, row.TaskId, row.ExecutorSessionId, row.Kind, row.Target, row.Branch, row.Title, row.Diff, row.Status, row.CreatedAt, row.DecidedAt);

    private static Error NotFound() => new("proposal.not_found", "The proposal does not exist.", ErrorType.NotFound);

    private static Error Decided() => new("proposal.decided", "The proposal was already merged or rejected.", ErrorType.Conflict);

    [LoggerMessage(Level = LogLevel.Information, Message = "Merging proposal {ProposalId} was refused: {Code}")]
    private partial void LogMergeRefused(Guid proposalId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The change of rejected proposal {ProposalId} could not be dropped: {Reason}")]
    private partial void LogDiscardFailed(Guid proposalId, string reason);
}
