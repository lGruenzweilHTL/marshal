using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Memory.Consolidation;

/// <summary>
/// Summarizes queued closed sessions one at a time, each as its owner. A failed summary is logged and skipped; the
/// session stays in the run history either way.
/// </summary>
public sealed partial class SessionSummaryWorker(
    SessionSummaryInbox inbox,
    IMemorySummarizer summarizer,
    IExecutionContextAccessor executionContext,
    ILogger<SessionSummaryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (ClosedSession closed in inbox.Reader.ReadAllAsync(stoppingToken))
        {
            using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = closed.Owner, Source = SessionSource.Task, IsInteractive = false });
            try
            {
                Result<MemorySummary> result = await summarizer.SummarizeAsync(closed.Session, stoppingToken);
                if (result.IsFailure)
                    LogSummaryFailed(logger, closed.Session.Value, result.Error.Code);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogSummaryCrashed(logger, exception, closed.Session.Value);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Summarizing session {SessionId} failed: {Code}")]
    private static partial void LogSummaryFailed(ILogger logger, Guid sessionId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Summarizing session {SessionId} crashed")]
    private static partial void LogSummaryCrashed(ILogger logger, Exception exception, Guid sessionId);
}
