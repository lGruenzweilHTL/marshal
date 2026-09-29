using System.Collections.Immutable;
using System.Threading.Channels;
using Marshal.Abstractions.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Events;

/// <summary>
/// The in-process event bus: one unbounded channel and one dispatcher that hands each event to the trigger matcher,
/// then to every subscriber, in publish order. A failing handler is logged and skipped.
/// </summary>
internal sealed partial class ChannelEventBus(ITriggerMatcher matcher, ILogger<ChannelEventBus> logger)
    : BackgroundService, IEventPublisher, IEventSubscriber
{
    private readonly Channel<MarshalEvent> _events = Channel.CreateUnbounded<MarshalEvent>(new UnboundedChannelOptions { SingleReader = true });
    private ImmutableArray<Func<MarshalEvent, CancellationToken, ValueTask>> _handlers = [];

    public void Publish(MarshalEvent @event) => _events.Writer.TryWrite(@event);

    public IDisposable Subscribe(Func<MarshalEvent, CancellationToken, ValueTask> handler)
    {
        ImmutableInterlocked.Update(ref _handlers, handlers => handlers.Add(handler));
        return new Subscription(() => ImmutableInterlocked.Update(ref _handlers, handlers => handlers.Remove(handler)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (MarshalEvent @event in _events.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await matcher.MatchAsync(@event, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    LogMatchFailed(@event.Source, @event.Type, exception);
                }

                foreach (Func<MarshalEvent, CancellationToken, ValueTask> handler in _handlers)
                {
                    try
                    {
                        await handler(@event, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        LogHandlerFailed(@event.Source, @event.Type, exception);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Matching event {Source} {Type} failed")]
    private partial void LogMatchFailed(string source, string type, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "An event subscriber failed on {Source} {Type}")]
    private partial void LogHandlerFailed(string source, string type, Exception exception);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                dispose();
        }
    }
}
