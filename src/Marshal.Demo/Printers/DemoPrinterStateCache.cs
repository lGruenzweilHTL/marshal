using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Hosting;

namespace Marshal.Demo.Printers;

/// <summary>
/// The simulated printer's state, computed from the job and the clock. Subscribers hear about changes every
/// <see cref="Interval"/> and right after a command, which drives the printer events and the live view.
/// </summary>
public sealed class DemoPrinterStateCache(DemoWorld world) : IPrinterStateCache, IHostedService, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly Lock _gate = new();
    private readonly List<Action<string, PrinterState?>> _subscribers = [];
    private ITimer? _timer;

    public Result<PrinterState> Get(string printer) =>
        string.Equals(printer, DemoPrinter.Name, StringComparison.OrdinalIgnoreCase) ? world.Printer.State() : PrinterErrors.NotFound(printer);

    public IDisposable Subscribe(Action<string, PrinterState?> changed)
    {
        lock (_gate)
            _subscribers.Add(changed);
        return new Subscription(() =>
        {
            lock (_gate)
                _subscribers.Remove(changed);
        });
    }

    /// <summary>Tells every subscriber the current state.</summary>
    public void Refresh()
    {
        PrinterState state = world.Printer.State();
        Action<string, PrinterState?>[] subscribers;
        lock (_gate)
            subscribers = [.. _subscribers];
        foreach (Action<string, PrinterState?> subscriber in subscribers)
            subscriber(DemoPrinter.Name, state);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = world.Time.CreateTimer(_ => Refresh(), null, TimeSpan.Zero, Interval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
