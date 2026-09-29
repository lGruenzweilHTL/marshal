using Marshal.Abstractions.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Deliveries;

/// <summary>
/// Re-checks waiting deliveries on every HA state change and periodically (deadlines, and state changes missed
/// while HA was disconnected).
/// </summary>
internal sealed partial class ConditionalDeliveryTracker(
    IDeliveryDispatcher dispatcher,
    IEventSubscriber events,
    TimeProvider time,
    ILogger<ConditionalDeliveryTracker> logger) : BackgroundService
{
    /// <summary>How often deadlines and conditions are checked without a state change.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IDisposable subscription = events.Subscribe(async (@event, cancellationToken) =>
        {
            if (@event is { Source: "ha", Type: "state_changed", Entity: { } entity })
                await ReviewAsync(entity, cancellationToken);
        });

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(CheckInterval, time, stoppingToken);
                await ReviewAsync(null, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task ReviewAsync(string? entity, CancellationToken cancellationToken)
    {
        try
        {
            await dispatcher.ReviewPendingAsync(entity, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogReviewFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking waiting deliveries failed")]
    private partial void LogReviewFailed(Exception exception);
}
