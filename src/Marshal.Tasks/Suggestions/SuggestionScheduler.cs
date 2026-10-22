using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Suggestions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Suggestions;

/// <summary>
/// Runs the suggestion detectors: on matching events from the bus, on their intervals, and once a minute the expiry
/// pass. Also hands the companion app's Accept / Dismiss push actions to the responder.
/// </summary>
public sealed partial class SuggestionScheduler(
    IEnumerable<ISuggestionDetector> detectors,
    SuggestionPipeline pipeline,
    SuggestionResponder responder,
    IEventSubscriber events,
    TimeProvider time,
    ILogger<SuggestionScheduler> logger) : BackgroundService
{
    /// <summary>How often intervals and expiry are checked.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly IReadOnlyList<ISuggestionDetector> _detectors = [.. detectors];
    private IDisposable? _subscription;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = events.Subscribe(OnEventAsync);
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _subscription?.Dispose();
        base.Dispose();
    }

    /// <summary>Handles one event from the bus: push actions, then detectors whose triggers match.</summary>
    public async ValueTask OnEventAsync(MarshalEvent @event, CancellationToken cancellationToken)
    {
        if (@event is { Source: "ha", Type: "mobile_app_notification_action" }
            && @event.Payload.ValueKind == JsonValueKind.Object
            && @event.Payload.TryGetProperty("action", out JsonElement action)
            && action.ValueKind == JsonValueKind.String)
        {
            await responder.HandlePushActionAsync(action.GetString()!, cancellationToken);
            return;
        }

        foreach (ISuggestionDetector detector in _detectors.Where(detector => detector.Triggers.Matches(@event)))
            await RunAsync(detector, @event, cancellationToken);
    }

    /// <summary>One tick: interval detectors that are due, then expiry.</summary>
    public async Task TickAsync(Dictionary<string, DateTimeOffset> nextRuns, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach (ISuggestionDetector detector in _detectors)
        {
            if (detector.Triggers.Interval is not { } interval)
                continue;
            if (nextRuns.TryGetValue(detector.Id, out DateTimeOffset next) && next > now)
                continue;

            nextRuns[detector.Id] = now + interval;
            await RunAsync(detector, null, cancellationToken);
        }

        try
        {
            await pipeline.ExpireAsync(_detectors, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogExpiryFailed(exception);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRuns = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TickInterval, time, stoppingToken);
                await TickAsync(nextRuns, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task RunAsync(ISuggestionDetector detector, MarshalEvent? @event, CancellationToken cancellationToken)
    {
        try
        {
            await pipeline.EvaluateAsync(detector, @event, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDetectorFailed(exception, detector.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Suggestion detector {Detector} failed")]
    private partial void LogDetectorFailed(Exception exception, string detector);

    [LoggerMessage(Level = LogLevel.Error, Message = "Expiring suggestions failed")]
    private partial void LogExpiryFailed(Exception exception);
}
