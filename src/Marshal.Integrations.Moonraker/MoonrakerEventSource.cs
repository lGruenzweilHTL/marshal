using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Hosting;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Publishes semantic printer events derived from state transitions in <see cref="IPrinterStateCache"/>: job state
/// changes (<c>started</c>, <c>paused</c>, <c>resumed</c>, <c>complete</c>, <c>error</c>, <c>cancelled</c>),
/// <c>klippy_shutdown</c>, <c>progress</c> milestones every 25 % and <c>first_layer_done</c> (when the slicer reports
/// layers). Temperatures never produce events. The last known state survives a disconnect, so a transition missed
/// while offline is published when the reconnect re-reads the state. Printer events are instance-wide (no owner).
/// </summary>
public sealed class MoonrakerEventSource(IPrinterStateCache cache, IEventPublisher publisher, TimeProvider time) : IHostedService, IEventSource
{
    public const string Name = "printer";

    private static readonly int[] _milestones = [25, 50, 75];

    private readonly Dictionary<string, PrinterState> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _online = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _subscription;

    public string Source => Name;

    public bool IsConnected
    {
        get
        {
            lock (_last)
                return _online.Count > 0;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = cache.Subscribe(Changed);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    private void Changed(string printer, PrinterState? state)
    {
        List<(string Type, int? Milestone)> events;
        lock (_last)
        {
            if (state is null)
            {
                _online.Remove(printer);
                return;
            }

            _online.Add(printer);
            events = _last.TryGetValue(printer, out PrinterState? previous) ? Transitions(previous, state) : [];
            _last[printer] = state;
        }

        foreach ((string type, int? milestone) in events)
            publisher.Publish(new MarshalEvent(Name, type, printer, Payload(state, milestone), null, time.GetUtcNow()));
    }

    internal static List<(string Type, int? Milestone)> Transitions(PrinterState previous, PrinterState current)
    {
        var events = new List<(string, int?)>();
        if (current.KlippyState == "shutdown" && previous.KlippyState != "shutdown")
            events.Add(("klippy_shutdown", null));

        if (current.PrintState != previous.PrintState)
        {
            string? type = current.PrintState switch
            {
                "printing" => previous.PrintState == "paused" ? "resumed" : "started",
                "paused" => "paused",
                "complete" => "complete",
                "error" => "error",
                "cancelled" => "cancelled",
                _ => null,
            };
            if (type is not null)
                events.Add((type, null));
        }

        if (current.PrintState == "printing" && previous.PrintState is "printing" or "paused")
        {
            if (current.CurrentLayer >= 2 && previous.CurrentLayer is null or < 2)
                events.Add(("first_layer_done", null));

            foreach (int milestone in _milestones)
            {
                if (previous.Progress * 100 < milestone && current.Progress * 100 >= milestone)
                    events.Add(("progress", milestone));
            }
        }

        return events;
    }

    private static JsonElement Payload(PrinterState state, int? milestone) => JsonSerializer.SerializeToElement(new
    {
        printer = state.Printer,
        filename = state.Filename,
        progress = Math.Round(state.Progress, 3),
        layer = state.CurrentLayer,
        totalLayers = state.TotalLayers,
        message = state.KlippyState == "shutdown" ? state.StateMessage ?? state.Message : state.Message,
        milestone,
    });
}
