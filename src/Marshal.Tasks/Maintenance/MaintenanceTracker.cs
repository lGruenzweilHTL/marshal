using System.Globalization;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Notifications;
using Marshal.Data;
using Marshal.Data.Maintenance;
using Marshal.Tasks.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Maintenance;

/// <summary>Where a maintenance item stands.</summary>
/// <param name="Id">Item id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Usage">Counter value minus the baseline; null before the first read.</param>
/// <param name="Every">The interval.</param>
/// <param name="WarnBefore">The early warning, if any.</param>
/// <param name="Unit">The counter's unit, if known.</param>
/// <param name="State"><c>ok</c>, <c>soon</c> or <c>due</c>.</param>
/// <param name="LastDoneAt">When the user marked it done; null until then.</param>
/// <param name="Error">Why the counter could not be read just now; null when it was.</param>
public sealed record MaintenanceItemStatus(
    string Id, string Name, decimal? Usage, decimal Every, decimal? WarnBefore, string? Unit, string State, DateTimeOffset? LastDoneAt, string? Error = null);

/// <summary>
/// Tracks <c>maintenance.items</c> for the instance owner: reads each counter on HA state changes of its entity, on
/// printer job completion and hourly; stores a baseline (the current value on first sight, reset when the counter
/// drops); sends one <c>normal</c> notification when usage reaches <c>every - warnBefore</c> and one when it reaches
/// <c>every</c>. <see cref="DoneAsync"/> sets the baseline to the current value.
/// </summary>
public sealed partial class MaintenanceTracker(
    IOptions<MaintenanceOptions> options,
    IEnumerable<IMaintenanceCounterSource> sources,
    IInstanceOwner instanceOwner,
    IUserPreferencesStore preferences,
    INotificationSender notifications,
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IEventSubscriber events,
    TimeProvider time,
    ILogger<MaintenanceTracker> logger) : BackgroundService
{
    /// <summary>Notification source.</summary>
    public const string Source = "maintenance";

    public static readonly TimeSpan ReadInterval = TimeSpan.FromHours(1);

    private readonly IReadOnlyList<IMaintenanceCounterSource> _sources = [.. sources];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IDisposable? _subscription;

    public IReadOnlyDictionary<string, MaintenanceItemOptions> Items => options.Value.Items;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (Items.Count > 0)
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
        _lock.Dispose();
        base.Dispose();
    }

    /// <summary>Reads the items the event concerns: an HA entity's state change, or a printer's completed job.</summary>
    public async ValueTask OnEventAsync(MarshalEvent @event, CancellationToken cancellationToken)
    {
        IEnumerable<string> concerned = @event switch
        {
            { Source: "ha", Type: "state_changed", Entity: { } entity } =>
                Items.Where(item => string.Equals(item.Value.Source.Ha, entity, StringComparison.Ordinal)).Select(item => item.Key),
            { Source: "printer", Type: "complete", Entity: { } printer } =>
                Items.Where(item => string.Equals(item.Value.Source.Printer, printer, StringComparison.OrdinalIgnoreCase)).Select(item => item.Key),
            _ => [],
        };
        foreach (string id in concerned.ToList())
            await CheckAsync(id, cancellationToken);
    }

    /// <summary>Reads every item.</summary>
    public async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        foreach (string id in Items.Keys.ToList())
            await CheckAsync(id, cancellationToken);
    }

    /// <summary>Reads one item's counter, moves its baseline on a reset and notifies once when it is due soon or due.</summary>
    public async Task<MaintenanceItemStatus?> CheckAsync(string id, CancellationToken cancellationToken)
    {
        if (!Items.TryGetValue(id, out MaintenanceItemOptions? item))
            return null;

        OwnerId owner = await instanceOwner.GetAsync(cancellationToken);
        using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(owner));
        Result<MaintenanceReading> reading = await ReadAsync(item, cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
            MaintenanceState? state = await db.MaintenanceStates.FirstOrDefaultAsync(row => row.ItemId == id, cancellationToken);
            if (reading.IsFailure)
            {
                LogReadFailed(id, reading.Error.Message);
                return Status(id, item, state, reading.Error.Message);
            }

            MaintenanceReading value = reading.Value;
            if (state is null)
            {
                state = new MaintenanceState { OwnerId = owner, ItemId = id, Baseline = value.Value };
                db.MaintenanceStates.Add(state);
            }
            else if (value.Value < state.Baseline)
            {
                // The counter was reset (a replaced sensor, a cleared history): usage continues from the new value.
                state.Baseline = value.Value;
                state.NotifiedSoon = false;
                state.NotifiedDue = false;
            }

            state.LastValue = value.Value;
            state.Unit = value.Unit ?? state.Unit;
            state.LastReadAt = time.GetUtcNow();
            string level = Level(item, state.LastValue - state.Baseline);
            bool notifyDue = level == "due" && !state.NotifiedDue;
            bool notifySoon = level == "soon" && !state.NotifiedSoon;
            if (notifyDue)
                (state.NotifiedDue, state.NotifiedSoon) = (true, true);
            else if (notifySoon)
                state.NotifiedSoon = true;
            await db.SaveChangesAsync(cancellationToken);

            if (notifyDue || notifySoon)
                await NotifyAsync(owner, id, item, state, due: notifyDue, cancellationToken);
            return Status(id, item, state, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Marks an item done: the baseline becomes the current counter value and the last-done time now.</summary>
    public async Task<Result<MaintenanceItemStatus>> DoneAsync(string id, CancellationToken cancellationToken)
    {
        if (!Items.TryGetValue(id, out MaintenanceItemOptions? item))
            return MaintenanceErrors.UnknownItem(id, Items.Keys);

        Result<MaintenanceReading> reading = await ReadAsync(item, cancellationToken);
        if (reading.IsFailure)
            return reading.Error;

        OwnerId owner = await instanceOwner.GetAsync(cancellationToken);
        using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(owner));
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
            MaintenanceState? state = await db.MaintenanceStates.FirstOrDefaultAsync(row => row.ItemId == id, cancellationToken);
            if (state is null)
            {
                state = new MaintenanceState { OwnerId = owner, ItemId = id };
                db.MaintenanceStates.Add(state);
            }

            DateTimeOffset now = time.GetUtcNow();
            state.Baseline = reading.Value.Value;
            state.LastValue = reading.Value.Value;
            state.Unit = reading.Value.Unit ?? state.Unit;
            state.LastReadAt = now;
            state.LastDoneAt = now;
            state.NotifiedSoon = false;
            state.NotifiedDue = false;
            await db.SaveChangesAsync(cancellationToken);
            return Status(id, item, state, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Every item, read now; due items first, then soon, then by name.</summary>
    public async Task<IReadOnlyList<MaintenanceItemStatus>> StatusAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<MaintenanceItemStatus>();
        foreach (string id in Items.Keys.ToList())
        {
            if (await CheckAsync(id, cancellationToken) is { } status)
                statuses.Add(status);
        }

        return [.. statuses.OrderBy(status => status.State switch { "due" => 0, "soon" => 1, _ => 2 }).ThenBy(status => status.Name, StringComparer.CurrentCulture)];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Items.Count == 0)
            return;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(ReadInterval, time, stoppingToken);
                try
                {
                    await CheckAllAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogCheckFailed(exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task<Result<MaintenanceReading>> ReadAsync(MaintenanceItemOptions item, CancellationToken cancellationToken)
    {
        MaintenanceSource source = item.Source.ToSource();
        return _sources.FirstOrDefault(candidate => candidate.Handles(source)) is { } reader
            ? await reader.ReadAsync(source, cancellationToken)
            : new Error("maintenance.source_unavailable", "The integration behind this counter is not configured.", ErrorType.Unavailable);
    }

    private static string Level(MaintenanceItemOptions item, decimal usage) =>
        usage >= item.Every ? "due"
        : item.WarnBefore is { } warn && usage >= item.Every - warn ? "soon"
        : "ok";

    private static MaintenanceItemStatus Status(string id, MaintenanceItemOptions item, MaintenanceState? state, string? error)
    {
        decimal? usage = state is null ? null : Math.Round(state.LastValue - state.Baseline, 1, MidpointRounding.AwayFromZero);
        return new MaintenanceItemStatus(id, item.Name, usage, item.Every, item.WarnBefore, state?.Unit, Level(item, usage ?? 0), state?.LastDoneAt, error);
    }

    private async Task NotifyAsync(OwnerId owner, string id, MaintenanceItemOptions item, MaintenanceState state, bool due, CancellationToken cancellationToken)
    {
        bool german = (await preferences.GetAsync(owner, cancellationToken)).Language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        string used = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(state.LastValue - state.Baseline, 1)} / {item.Every}{(state.Unit is { } unit ? " " + unit : "")}");
        string text = (due, german) switch
        {
            (true, true) => $"Wartung fällig: {item.Name} ({used}).",
            (true, false) => $"Maintenance due: {item.Name} ({used}).",
            (false, true) => $"Wartung bald fällig: {item.Name} ({used}).",
            (false, false) => $"Maintenance due soon: {item.Name} ({used}).",
        };
        Result<Guid> sent = await notifications.SendAsync(new NotificationRequest
        {
            Owner = owner,
            Source = Source,
            Priority = NotificationPriority.Normal,
            Text = text,
            Link = "",
            Tag = $"maintenance-{id}",
        }, cancellationToken);
        if (sent.IsFailure)
            LogNotifyFailed(id, sent.Error.Message);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance item {Item}: counter not read: {Reason}")]
    private partial void LogReadFailed(string item, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Maintenance notification for {Item} failed: {Reason}")]
    private partial void LogNotifyFailed(string item, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading maintenance counters failed")]
    private partial void LogCheckFailed(Exception exception);
}

/// <summary>Errors of the maintenance tools.</summary>
public static class MaintenanceErrors
{
    public static Error UnknownItem(string id, IEnumerable<string> known) =>
        new("maintenance.unknown_item", $"There is no maintenance item '{id}'. Items: {string.Join(", ", known)}.", ErrorType.NotFound);
}

/// <summary>Serializes statuses for tools and the API.</summary>
internal static class MaintenanceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}
