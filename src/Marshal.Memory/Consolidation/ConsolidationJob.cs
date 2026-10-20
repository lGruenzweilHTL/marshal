using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Memory.Idle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Consolidation;

/// <summary>
/// Nightly memory consolidation (system job <c>consolidation</c>): every session of every owner closed in the last
/// day that has no successful summary run yet goes through the same <see cref="IMemorySummarizer"/> as session close,
/// as its owner. So sessions the close path missed (restart, model down, failure) are caught up, and sessions it
/// already summarized cost nothing. The summarizer reads user turns and replies only, never tool results. Then the
/// idle-time jobs run for every owner, in registration order, each on its own so one failure does not stop the others.
/// </summary>
public sealed partial class ConsolidationJob(
    IDbContextFactory<MarshalDbContext> contexts,
    IMemorySummarizer summarizer,
    IEnumerable<IIdleMemoryJob> idleJobs,
    IUserDirectory users,
    IExecutionContextAccessor executionContext,
    IOptions<MemoryOptions> options,
    TimeProvider time,
    ILogger<ConsolidationJob> logger) : ISystemJob
{
    public const string JobName = "consolidation";

    private static readonly TimeSpan _window = TimeSpan.FromDays(1);

    public string Name => JobName;

    public async Task<Result<string>> RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset since = time.GetUtcNow() - _window;
        string summaries = options.Value.Summarizer.Enabled
            ? await SummarizeAsync(since, cancellationToken)
            : "Skipped: the memory summarizer is disabled.";
        int failed = await RunIdleJobsAsync(since, cancellationToken);
        return failed == 0 ? summaries : string.Create(CultureInfo.InvariantCulture, $"{summaries} Idle-time jobs: {failed} failed.");
    }

    private async Task<int> RunIdleJobsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        List<IIdleMemoryJob> jobs = [.. idleJobs];
        if (jobs.Count == 0)
            return 0;

        int failed = 0;
        foreach (OwnerId owner in await users.ListAsync(cancellationToken))
        {
            using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false });
            foreach (IIdleMemoryJob job in jobs)
            {
                try
                {
                    Result<int> result = await job.RunAsync(since, cancellationToken);
                    if (result.IsFailure)
                        LogIdleJobSkipped(job.Name, owner.Value, result.Error.Code);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed++;
                    LogIdleJobFailed(exception, job.Name, owner.Value);
                }
            }
        }

        return failed;
    }

    private async Task<string> SummarizeAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        List<ClosedSession> sessions;
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            // A sanctioned cross-owner read of ids and owners only; each session is then summarized as its owner.
            sessions = await db.Sessions.IgnoreQueryFilters().AsNoTracking()
                .Where(session => session.ClosedAt != null && session.ClosedAt >= since)
                .Where(session => !db.Runs.IgnoreQueryFilters().Any(run =>
                    run.SessionId == session.Id && run.Trigger == LlmMemorySummarizer.Trigger && run.Outcome == RunOutcome.Ok))
                .Select(session => new ClosedSession(session.OwnerId, session.Id))
                .ToListAsync(cancellationToken);
        }

        int consolidated = 0;
        foreach (ClosedSession closed in sessions)
        {
            using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = closed.Owner, Source = SessionSource.Task, IsInteractive = false });
            Result<MemorySummary> summary = await summarizer.SummarizeAsync(closed.Session, cancellationToken);
            if (summary.IsSuccess)
                consolidated++;
            else
                LogSummaryFailed(closed.Session.Value, summary.Error.Code);
        }

        return string.Create(CultureInfo.InvariantCulture, $"Consolidated {consolidated} of {sessions.Count} sessions closed in the last day.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idle-time memory job {Job} for owner {OwnerId} did nothing: {Code}")]
    private partial void LogIdleJobSkipped(string job, Guid ownerId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Idle-time memory job {Job} for owner {OwnerId} failed")]
    private partial void LogIdleJobFailed(Exception exception, string job, Guid ownerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nightly consolidation of session {SessionId} failed: {Code}")]
    private partial void LogSummaryFailed(Guid sessionId, string code);
}
