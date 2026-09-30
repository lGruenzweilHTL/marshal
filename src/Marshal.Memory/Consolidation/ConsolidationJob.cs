using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Consolidation;

/// <summary>
/// Nightly memory consolidation (system job <c>consolidation</c>): every session of every owner closed in the last
/// day that has no successful summary run yet goes through the same <see cref="IMemorySummarizer"/> as session close,
/// as its owner. So sessions the close path missed (restart, model down, failure) are caught up, and sessions it
/// already summarized cost nothing. The summarizer reads user turns and replies only, never tool results.
/// </summary>
public sealed partial class ConsolidationJob(
    IDbContextFactory<MarshalDbContext> contexts,
    IMemorySummarizer summarizer,
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
        if (!options.Value.Summarizer.Enabled)
            return "Skipped: the memory summarizer is disabled.";

        DateTimeOffset since = time.GetUtcNow() - _window;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nightly consolidation of session {SessionId} failed: {Code}")]
    private partial void LogSummaryFailed(Guid sessionId, string code);
}
