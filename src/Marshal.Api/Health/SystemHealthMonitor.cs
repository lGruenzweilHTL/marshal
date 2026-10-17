using Marshal.Abstractions.Health;

namespace Marshal.Api.Health;

/// <summary>
/// Re-evaluates the health warnings periodically so changes reach the <c>health</c> topic without a client polling.
/// </summary>
internal sealed partial class SystemHealthMonitor(ISystemHealth health, TimeProvider time, ILogger<SystemHealthMonitor> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await health.GetWarningsAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Evaluating health warnings failed")]
    private partial void LogFailed(Exception exception);
}
