using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Notifications;

/// <summary>
/// Delivers held notifications when quiet hours end and coalesced ones when their window closes.
/// </summary>
internal sealed partial class NotificationDueLoop(NotificationRouter router, TimeProvider time, ILogger<NotificationDueLoop> logger) : BackgroundService
{
    private static readonly TimeSpan _maxSleep = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await router.DeliverDueAsync(stoppingToken);
                DateTimeOffset? next = await router.NextDueAsync(stoppingToken);
                TimeSpan sleep = next is { } due ? due - time.GetUtcNow() : _maxSleep;
                if (sleep > TimeSpan.Zero)
                    await router.WaitForChangeAsync(sleep < _maxSleep ? sleep : _maxSleep, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogFailed(exception);
                await Task.Delay(_maxSleep, time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivering due notifications failed")]
    private partial void LogFailed(Exception exception);
}
