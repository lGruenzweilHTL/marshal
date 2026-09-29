using System.Threading.Channels;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Printers;

namespace Marshal.Api.Printers;

/// <summary>
/// Forwards printer state changes to the <c>printer</c> live topic. Printers belong to the instance, so every user
/// receives them; a null state means the printer went offline.
/// </summary>
internal sealed class PrinterLiveEvents(IPrinterStateCache states, IUserDirectory users, ILiveEventPublisher publisher, TimeProvider time) : BackgroundService
{
    public const string Topic = "printer";

    public const string StateEvent = "printer.state";

    private static readonly TimeSpan _userRefresh = TimeSpan.FromMinutes(1);

    private readonly Channel<(string Printer, PrinterState? State)> _changes =
        Channel.CreateBounded<(string, PrinterState?)>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IDisposable subscription = states.Subscribe((printer, state) => _changes.Writer.TryWrite((printer, state)));
        IReadOnlyList<OwnerId> owners = [];
        DateTimeOffset loadedAt = DateTimeOffset.MinValue;
        await foreach ((string printer, PrinterState? state) in _changes.Reader.ReadAllAsync(stoppingToken))
        {
            if (time.GetUtcNow() - loadedAt > _userRefresh)
            {
                owners = await users.ListAsync(stoppingToken);
                loadedAt = time.GetUtcNow();
            }

            foreach (OwnerId owner in owners)
                publisher.Publish(new LiveEvent(Topic, StateEvent, Guid.CreateVersion7().ToString(), owner, new { printer, state }));
        }
    }
}
