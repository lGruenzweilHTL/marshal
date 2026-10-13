using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Integrations.HomeAssistant;

namespace Marshal.Demo;

/// <summary>A state change of a simulated entity.</summary>
/// <param name="EntityId">The entity.</param>
/// <param name="From">The previous state.</param>
/// <param name="To">The new state.</param>
/// <param name="Attributes">The new attributes.</param>
public sealed record DemoStateChange(string EntityId, string? From, string To, JsonElement Attributes);

/// <summary>
/// The simulated Home Assistant: areas, floors, entities and their states, and the calendars. Every entity is
/// exposed to Assist. State changes replace the snapshot and raise <see cref="Changed"/>.
/// </summary>
public sealed class DemoHome
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private HaSnapshot _snapshot;

    internal DemoHome(TimeProvider time)
    {
        _time = time;
        HomeFile file = DemoResources.Read<HomeFile>("home.json");
        DateTimeOffset now = time.GetUtcNow();
        _snapshot = new HaSnapshot
        {
            Floors = file.Floors.ToImmutableDictionary(floor => floor.Id, floor => new HaFloor(floor.Id, floor.Name, floor.Aliases ?? [])),
            Areas = file.Areas.ToImmutableDictionary(area => area.Id, area => new HaArea(area.Id, area.Name, area.Aliases ?? [], area.Floor)),
            Devices = ImmutableDictionary<string, HaDevice>.Empty,
            Entities = file.Entities.ToImmutableDictionary(entity => entity.Id, entity => new HaEntity(entity.Id, entity.Name, [], entity.Area, null, entity.DeviceClass, Exposed: true)),
            States = file.Entities.ToImmutableDictionary(entity => entity.Id, entity => new HaState(entity.Id, entity.State, Attributes(entity.Name, entity.DeviceClass, entity.Attributes), now)),
            TimeZone = file.TimeZone,
            Currency = file.Currency,
        };
        var zone = TimeZoneInfo.FindSystemTimeZoneById(file.TimeZone ?? "UTC");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        Calendars = [.. file.Calendars.Select(calendar => new DemoCalendar(calendar.Id, calendar.Name, [.. calendar.Events.Select(item => item.ToEvent(today, zone))]))];
    }

    /// <summary>Raised after a state changed (not for writes that keep the state and attributes).</summary>
    public event Action<DemoStateChange>? Changed;

    public HaSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return _snapshot;
        }
    }

    public IReadOnlyList<DemoCalendar> Calendars { get; }

    /// <summary>Sets an entity's state and merges <paramref name="attributes"/> into its attributes (null removes one).</summary>
    public Result<HaState> Set(string entityId, string state, IReadOnlyDictionary<string, object?>? attributes)
    {
        DemoStateChange? change;
        HaState updated;
        lock (_gate)
        {
            if (!_snapshot.States.TryGetValue(entityId, out HaState? current))
                return new Error("ha.unknown_entity", $"There is no entity '{entityId}'.", ErrorType.NotFound);

            JsonObject merged = JsonNode.Parse(current.Attributes.GetRawText())!.AsObject();
            foreach ((string key, object? value) in attributes ?? new Dictionary<string, object?>())
            {
                if (value is null)
                    merged.Remove(key);
                else
                    merged[key] = JsonSerializer.SerializeToNode(value);
            }

            JsonElement newAttributes = JsonSerializer.SerializeToElement(merged);
            if (current.State == state && JsonNode.DeepEquals(JsonNode.Parse(current.Attributes.GetRawText()), merged))
                return current;

            updated = new HaState(entityId, state, newAttributes, _time.GetUtcNow());
            _snapshot = _snapshot with { States = _snapshot.States.SetItem(entityId, updated) };
            change = current.State == state ? null : new DemoStateChange(entityId, current.State, state, newAttributes);
        }

        if (change is not null)
            Changed?.Invoke(change);
        return updated;
    }

    /// <summary>Timers started through the timer intents, per device; expired ones are dropped when read.</summary>
    public DemoTimers Timers { get; } = new();

    /// <summary>The home's time zone (from the demo content).</summary>
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(Snapshot.TimeZone ?? "UTC");

    /// <summary>Runs <paramref name="change"/> on the calendars under the home's lock.</summary>
    public T WithCalendars<T>(Func<IReadOnlyList<DemoCalendar>, T> change)
    {
        lock (_gate)
            return change(Calendars);
    }

    /// <summary>Adds a zone entity (for example created through <c>zone/create</c>).</summary>
    public HaState AddZone(string id, string name, double latitude, double longitude, int radius)
    {
        var attributes = new Dictionary<string, JsonElement>
        {
            ["latitude"] = JsonSerializer.SerializeToElement(latitude),
            ["longitude"] = JsonSerializer.SerializeToElement(longitude),
            ["radius"] = JsonSerializer.SerializeToElement(radius),
            ["passive"] = JsonSerializer.SerializeToElement(false),
        };
        var state = new HaState(id, "0", Attributes(name, null, attributes), _time.GetUtcNow());
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                Entities = _snapshot.Entities.SetItem(id, new HaEntity(id, name, [], null, null, null, Exposed: true)),
                States = _snapshot.States.SetItem(id, state),
            };
        }

        return state;
    }

    private static JsonElement Attributes(string name, string? deviceClass, Dictionary<string, JsonElement>? attributes)
    {
        var result = new JsonObject { ["friendly_name"] = name };
        if (deviceClass is not null)
            result["device_class"] = deviceClass;
        foreach ((string key, JsonElement value) in attributes ?? [])
            result[key] = JsonNode.Parse(value.GetRawText());
        return JsonSerializer.SerializeToElement(result);
    }

    private sealed record HomeFile(string? TimeZone, string? Currency, List<FloorFile> Floors, List<AreaFile> Areas, List<EntityFile> Entities, List<CalendarFile> Calendars);

    private sealed record FloorFile(string Id, string Name, List<string>? Aliases);

    private sealed record AreaFile(string Id, string Name, string? Floor, List<string>? Aliases);

    private sealed record EntityFile(string Id, string Name, string? Area, string? DeviceClass, string State, Dictionary<string, JsonElement>? Attributes);

    private sealed record CalendarFile(string Id, string Name, List<CalendarEventFile> Events);

    private sealed record CalendarEventFile(string Summary, int Day, bool AllDay, string? Start, string? End, string? Location)
    {
        public DemoCalendarEvent ToEvent(DateOnly today, TimeZoneInfo zone)
        {
            DateOnly date = today.AddDays(Day);
            if (AllDay)
                return new DemoCalendarEvent(Guid.NewGuid().ToString("N"), Summary, CalendarTime.AllDay(date), CalendarTime.AllDay(date.AddDays(1)), Location);

            return new DemoCalendarEvent(Guid.NewGuid().ToString("N"), Summary, CalendarTime.At(At(date, Start!, zone)), CalendarTime.At(At(date, End!, zone)), Location);
        }

        private static DateTimeOffset At(DateOnly date, string time, TimeZoneInfo zone)
        {
            var local = date.ToDateTime(TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture));
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
    }
}

/// <summary>A simulated calendar; events created through the calendar tools are added to <see cref="Events"/>.</summary>
public sealed record DemoCalendar(string Id, string Name, List<DemoCalendarEvent> Events);

/// <summary>A simulated calendar event; all-day events end on the day after.</summary>
public sealed record DemoCalendarEvent(string Id, string Summary, CalendarTime Start, CalendarTime End, string? Location, string? Description = null);
