using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Approvals;

/// <summary>
/// Expires overdue background approvals and resumes their tasks with a rejection.
/// </summary>
public sealed partial class ApprovalExpiryLoop(ApprovalQueue approvals, TimeProvider time, ILogger<ApprovalExpiryLoop> logger) : BackgroundService
{
    /// <summary>How often overdue approvals are looked for.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(CheckInterval, time, stoppingToken);
                try
                {
                    await approvals.ExpireDueAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogExpiryFailed(exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Expiring overdue approvals failed")]
    private partial void LogExpiryFailed(Exception exception);
}
