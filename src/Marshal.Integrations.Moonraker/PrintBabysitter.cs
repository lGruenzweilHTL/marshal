using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// The print babysitter: a system component, not an agent task. For printers with <c>babysitter.enabled</c> it
/// watches from <c>started</c> / <c>resumed</c> until <c>paused</c>, <c>complete</c>, <c>cancelled</c>, <c>error</c> or
/// a Klipper shutdown (and picks up a print already running at startup). Every interval it judges a snapshot with
/// <see cref="IPrintFailureDetector"/>; two positive verdicts in a row (a frame without a verdict breaks the row)
/// pause the print directly through <see cref="IPrinterClient"/> and send a <c>critical</c> notification with the
/// snapshot as <c>system:babysitter</c>. Only configuration turns it on or off; the agent can read its state.
/// </summary>
public sealed partial class PrintBabysitter(
    IOptions<PrinterOptions> options,
    IEventSubscriber events,
    IPrinterStateCache states,
    ISnapshotSource snapshots,
    ISnapshotLinks links,
    IPrintFailureDetector detector,
    IPrinterClient client,
    INotificationSender notifications,
    IInstanceOwner instanceOwner,
    IUserProfileProvider profiles,
    IExecutionContextAccessor executionContext,
    TimeProvider time,
    ILogger<PrintBabysitter> logger) : IHostedService, IPrintBabysitterStatus, IDisposable
{
    /// <summary>Notification source of the babysitter (system-defined, so it may send <c>critical</c>).</summary>
    public const string Source = NotificationRequest.SystemPrefix + "babysitter";

    private const int KeptVerdicts = 5;
    private const int Strikes = 2;

    private static readonly HashSet<string> _startTypes = new(StringComparer.Ordinal) { "started", "resumed" };
    private static readonly HashSet<string> _stopTypes = new(StringComparer.Ordinal) { "paused", "complete", "cancelled", "error", "klippy_shutdown" };

    private readonly Dictionary<string, Watch> _watches = options.Value.Printers
        .Where(printer => printer.Babysitter.Enabled)
        .ToDictionary(printer => printer.Name, printer => new Watch(printer.Babysitter.Interval), StringComparer.OrdinalIgnoreCase);

    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = events.Subscribe(OnEventAsync);
        foreach (string printer in _watches.Keys)
        {
            if (states.Get(printer) is { IsSuccess: true, Value.PrintState: "printing" })
                Start(printer);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        List<Task> loops = [];
        foreach (string printer in _watches.Keys)
        {
            if (Stop(printer) is { } loop)
                loops.Add(loop);
        }

        await Task.WhenAll(loops).WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        foreach (string printer in _watches.Keys)
            _ = Stop(printer);
    }

    public BabysitterStatus? Get(string printer)
    {
        if (!_watches.TryGetValue(printer, out Watch? watch))
            return null;
        lock (watch)
            return new BabysitterStatus(watch.Cancel is not null, [.. watch.Verdicts], watch.PausedAt);
    }

    /// <summary>One check: snapshot, verdict, and the pause after the second positive verdict in a row.</summary>
    internal async Task CheckAsync(string printer, CancellationToken cancellationToken)
    {
        Watch watch = _watches[printer];
        Result<PrinterSnapshot> snapshot = await snapshots.CaptureAsync(printer, cancellationToken);
        Result<PrintVerdict> verdict = snapshot.IsSuccess
            ? await detector.DetectAsync(snapshot.Value, cancellationToken)
            : snapshot.Error;

        BabysitterCheck check = verdict.IsSuccess
            ? new BabysitterCheck(time.GetUtcNow(), verdict.Value.Failure ? "failure" : "ok", verdict.Value.Reason, snapshot.Value.Url)
            : new BabysitterCheck(time.GetUtcNow(), "unknown", verdict.Error.Message, snapshot.IsSuccess ? snapshot.Value.Url : null);
        bool pause;
        lock (watch)
        {
            watch.Verdicts.Enqueue(check);
            while (watch.Verdicts.Count > KeptVerdicts)
                watch.Verdicts.Dequeue();
            watch.Strikes = check.Outcome == "failure" ? watch.Strikes + 1 : 0;
            pause = watch.Strikes >= Strikes;
        }

        if (verdict.IsFailure)
            LogNoVerdict(printer, verdict.Error.Message);
        if (pause)
            await PauseAsync(printer, watch, snapshot.Value, verdict.Value.Reason);
    }

    private async Task PauseAsync(string printer, Watch watch, PrinterSnapshot snapshot, string reason)
    {
        Result paused = await client.SendAsync(printer, new PrinterCommand.Pause(), CancellationToken.None);
        lock (watch)
        {
            watch.PausedAt = time.GetUtcNow();
            watch.Strikes = 0;
        }

        LogPaused(printer, reason, paused.IsSuccess);
        OwnerId owner = await instanceOwner.GetAsync(CancellationToken.None);
        bool german = (await profiles.GetAsync(owner, CancellationToken.None)) is { IsSuccess: true, Value.Language: "de" };
        string text = german
            ? $"Der Druck auf {printer} sieht fehlgeschlagen aus ({reason}). " + (paused.IsSuccess ? "Ich habe ihn pausiert." : $"Pausieren ist fehlgeschlagen: {paused.Error.Message}")
            : $"The print on {printer} looks failed ({reason}). " + (paused.IsSuccess ? "I paused it." : $"Pausing failed: {paused.Error.Message}");

        using (executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false }))
        {
            Result<Guid> sent = await notifications.SendAsync(new NotificationRequest
            {
                Owner = owner,
                Source = Source,
                Priority = NotificationPriority.Critical,
                Text = text,
                Link = $"printers/{printer}",
                Image = links.Shared(snapshot.Printer, snapshot.Id),
                Tag = $"babysitter-{printer}",
            }, CancellationToken.None);
            if (sent.IsFailure)
                LogNotifyFailed(printer, sent.Error.Message);
        }

        // The pause ends this watch; a resumed print starts a new one.
        _ = Stop(printer);
    }

    private ValueTask OnEventAsync(MarshalEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Source != MoonrakerEventSource.Name || @event.Entity is not { } printer || !_watches.ContainsKey(printer))
            return ValueTask.CompletedTask;

        if (_startTypes.Contains(@event.Type))
            Start(printer);
        else if (_stopTypes.Contains(@event.Type))
            _ = Stop(printer);
        return ValueTask.CompletedTask;
    }

    private void Start(string printer)
    {
        Watch watch = _watches[printer];
        lock (watch)
        {
            if (watch.Cancel is not null)
                return;
            var cancel = new CancellationTokenSource();
            watch.Cancel = cancel;
            watch.Strikes = 0;
            // The timer starts now, so the first check is one interval after the print started.
            var timer = new PeriodicTimer(watch.Interval, time);
            watch.Loop = Task.Run(() => LoopAsync(printer, timer, cancel), CancellationToken.None);
        }

        LogStarted(printer);
    }

    private Task? Stop(string printer)
    {
        Watch watch = _watches[printer];
        lock (watch)
        {
            if (watch.Cancel is not { } cancel)
                return null;
            watch.Cancel = null;
            cancel.Cancel();
            LogStopped(printer);
            return watch.Loop;
        }
    }

    private async Task LoopAsync(string printer, PeriodicTimer timer, CancellationTokenSource cancel)
    {
        using CancellationTokenSource owned = cancel;
        using PeriodicTimer owns = timer;
        CancellationToken cancellationToken = cancel.Token;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await CheckAsync(printer, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogCheckFailed(printer, exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private sealed class Watch(TimeSpan interval)
    {
        public TimeSpan Interval { get; } = interval;

        public CancellationTokenSource? Cancel { get; set; }

        public Task? Loop { get; set; }

        public int Strikes { get; set; }

        public Queue<BabysitterCheck> Verdicts { get; } = new();

        public DateTimeOffset? PausedAt { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Babysitter watches the print on {Printer}")]
    private partial void LogStarted(string printer);

    [LoggerMessage(Level = LogLevel.Information, Message = "Babysitter stopped watching {Printer}")]
    private partial void LogStopped(string printer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Babysitter got no verdict for {Printer}: {Reason}")]
    private partial void LogNoVerdict(string printer, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Babysitter detected a failed print on {Printer} ({Reason}); paused: {Paused}")]
    private partial void LogPaused(string printer, string reason, bool paused);

    [LoggerMessage(Level = LogLevel.Error, Message = "Babysitter could not notify about {Printer}: {Reason}")]
    private partial void LogNotifyFailed(string printer, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Babysitter check of {Printer} failed")]
    private partial void LogCheckFailed(string printer, Exception exception);
}
