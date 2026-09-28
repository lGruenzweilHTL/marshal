using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Keeps <see cref="IHomeAssistantCache"/> fresh: connects, subscribes to state and registry events, loads
/// registries, exposure and states, then applies state changes and reloads the registries on registry events and
/// periodically (exposure changes have no event). After a disconnect the cache is empty until the reconnect has
/// reloaded everything, so callers never act on stale data.
/// </summary>
public sealed partial class HomeAssistantCache(
    IOptions<HomeAssistantOptions> options,
    IHomeAssistantSocketFactory sockets,
    HomeAssistantCacheTimings timings,
    TimeProvider time,
    ILogger<HomeAssistantCache> logger) : BackgroundService, IHomeAssistantCache
{
    private const string StateChanged = "state_changed";

    private static readonly string[] _registryEvents = ["entity_registry_updated", "device_registry_updated", "area_registry_updated", "floor_registry_updated"];

    private volatile HaSnapshot? _current;
    private volatile string? _lastError;

    public HaSnapshot? Current => _current;

    public string? LastError => _lastError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HomeAssistantOptions ha = options.Value;
        if (ha.Url is null)
            return;

        int attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using HaConnection connection = await HaConnection.ConnectAsync(sockets, ha.WebSocketUri(), ha.Token ?? "", timings.CommandTimeout, stoppingToken);
                await RunAsync(connection, () => attempt = 0, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
                LogDisconnected(exception.Message);
            }

            _current = null;
            try
            {
                await Task.Delay(timings.Backoff(attempt++), time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _current = null;
    }

    private async Task RunAsync(HaConnection connection, Action connected, CancellationToken stoppingToken)
    {
        // Subscribe first: events that arrive during the load are queued and applied afterwards.
        foreach (string eventType in _registryEvents.Prepend(StateChanged))
            await connection.CommandAsync("subscribe_events", new JsonObject { ["event_type"] = eventType }, stoppingToken);

        HaSnapshotBuilder.Registries registries = await LoadRegistriesAsync(connection, stoppingToken);
        ImmutableDictionary<string, HaState> states = HaSnapshotBuilder.States(await connection.CommandAsync("get_states", null, stoppingToken));
        _current = HaSnapshotBuilder.Build(registries, states);
        _lastError = null;
        connected();
        LogLoaded(_current.Entities.Count, _current.States.Count);

        while (true)
        {
            bool reload = false;
            using (var refreshDue = new CancellationTokenSource(timings.RegistryRefresh, time))
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, refreshDue.Token))
            {
                try
                {
                    if (!await connection.Events.WaitToReadAsync(wait.Token))
                        throw new HomeAssistantException("The connection to Home Assistant was closed.");
                }
                catch (OperationCanceledException) when (refreshDue.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    reload = true;
                }
            }

            while (connection.Events.TryRead(out JsonElement @event))
            {
                string? type = @event.TryGetProperty("event_type", out JsonElement found) ? found.GetString() : null;
                if (type == StateChanged && @event.TryGetProperty("data", out JsonElement data) && _current is { } current)
                    _current = HaSnapshotBuilder.ApplyStateChanged(current, data);
                else if (type is not null && _registryEvents.Contains(type))
                    reload = true;
            }

            if (reload && _current is { } before)
                _current = HaSnapshotBuilder.Build(await LoadRegistriesAsync(connection, stoppingToken), before.States);
        }
    }

    private static async Task<HaSnapshotBuilder.Registries> LoadRegistriesAsync(HaConnection connection, CancellationToken cancellationToken)
    {
        JsonElement config = await connection.CommandAsync("get_config", null, cancellationToken);
        JsonElement areas = await connection.CommandAsync("config/area_registry/list", null, cancellationToken);
        JsonElement floors = await connection.CommandAsync("config/floor_registry/list", null, cancellationToken);
        JsonElement devices = await connection.CommandAsync("config/device_registry/list", null, cancellationToken);
        JsonElement entities = await connection.CommandAsync("config/entity_registry/list", null, cancellationToken);
        JsonElement exposed = await connection.CommandAsync("homeassistant/expose_entity/list", null, cancellationToken);

        HashSet<string> registered = [.. entities.EnumerateArray().Select(entry => entry.GetProperty("entity_id").GetString()!)];
        JsonArray ids = [.. HaSnapshotBuilder.ExposedIds(exposed).Where(registered.Contains).Order(StringComparer.Ordinal).Select(id => (JsonNode)id)];
        JsonElement entries = ids.Count == 0
            ? default
            : await connection.CommandAsync("config/entity_registry/get_entries", new JsonObject { ["entity_ids"] = ids }, cancellationToken);

        string? timeZone = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("time_zone", out JsonElement zone) ? zone.GetString() : null;
        return new HaSnapshotBuilder.Registries(areas, floors, devices, entities, exposed, entries, timeZone);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Home Assistant cache loaded: {Entities} entities, {States} states")]
    private partial void LogLoaded(int entities, int states);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Home Assistant connection lost: {Reason}")]
    private partial void LogDisconnected(string reason);
}
