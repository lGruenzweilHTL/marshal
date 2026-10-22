using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Calendar;

/// <summary>
/// Home Assistant <c>calendar.*</c> entities: the list and events over REST (<c>/api/calendars</c>, which includes
/// the event uid), creation through the <c>calendar.create_event</c> action and updates through the websocket command
/// <c>calendar/event/update</c> (there is no update action). What a calendar supports comes from the entity's
/// <c>supported_features</c> in the state cache. HA has no attendee data and cannot be told whether its integration
/// edits single occurrences, so these calendars take no update scope.
/// </summary>
public sealed partial class HomeAssistantCalendarProvider(
    IHomeAssistantClient client,
    IHomeAssistantCommands commands,
    IHomeAssistantCache cache,
    IOptions<HomeAssistantOptions> options) : ICalendarSource
{
    // CalendarEntityFeature in homeassistant/components/calendar/const.py.
    private const int CreateEvent = 1;
    private const int UpdateEvent = 4;

    public bool IsConfigured => options.Value.Url is not null;

    public bool Owns(string calendarId) => calendarId.StartsWith("calendar.", StringComparison.Ordinal);

    public CalendarOverview Overview
    {
        get
        {
            List<string> calendars = [.. cache.Current?.States.Keys.Where(id => id.StartsWith("calendar.", StringComparison.Ordinal)) ?? []];
            return new CalendarOverview(calendars.Count, calendars.Aggregate(CalendarCapabilities.None, (all, id) => all | Capabilities(id)));
        }
    }

    public async Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken)
    {
        Result<JsonElement> list = await client.GetAsync("api/calendars", cancellationToken);
        if (list.IsFailure)
            return CalendarErrors.Failed(list.Error.Message);

        return list.Value.EnumerateArray()
            .Select(entry => entry.GetProperty("entity_id").GetString()!)
            .Zip(list.Value.EnumerateArray().Select(entry => entry.TryGetProperty("name", out JsonElement name) ? name.GetString() : null))
            .Select(pair => new CalendarInfo(pair.First, pair.Second ?? pair.First, Capabilities(pair.First)))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        if (!EntityId().IsMatch(calendarId))
            return CalendarErrors.NotFound(calendarId);

        string query = $"?start={Uri.EscapeDataString(Iso(start))}&end={Uri.EscapeDataString(Iso(end))}";
        Result<JsonElement> events = await client.GetAsync($"api/calendars/{calendarId}{query}", cancellationToken);
        if (events.IsFailure)
        {
            return events.Error.Code == "ha.http_error" && events.Error.Message.Contains(" 404 ", StringComparison.Ordinal)
                ? CalendarErrors.NotFound(calendarId)
                : CalendarErrors.Failed(events.Error.Message);
        }

        return events.Value.EnumerateArray().Select(entry => new CalendarEvent(
            calendarId,
            Text(entry, "uid"),
            Text(entry, "recurrence_id"),
            Text(entry, "summary") ?? "",
            Time(entry.GetProperty("start"), zone),
            Time(entry.GetProperty("end"), zone),
            Text(entry, "description"),
            Text(entry, "location"))).ToList();
    }

    public async Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken)
    {
        Result supported = await RequireAsync(calendarId, CalendarCapabilities.Create, "creating", cancellationToken);
        if (supported.IsFailure)
            return supported;

        var service = new JsonObject { ["entity_id"] = calendarId, ["summary"] = data.Summary };
        if (data.Start.DateTime is { } startTime && data.End.DateTime is { } endTime)
        {
            service["start_date_time"] = Iso(startTime);
            service["end_date_time"] = Iso(endTime);
        }
        else
        {
            service["start_date"] = Date(data.Start);
            service["end_date"] = Date(data.End);
        }

        if (data.Description is not null)
            service["description"] = data.Description;
        if (data.Location is not null)
            service["location"] = data.Location;

        Result<JsonElement> created = await client.CallServiceAsync("calendar", "create_event", service, cancellationToken);
        return created.IsSuccess ? Result.Success() : CalendarErrors.Failed(created.Error.Message);
    }

    public async Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CalendarUpdateScope? scope, CancellationToken cancellationToken)
    {
        if (scope is not null)
            return CalendarErrors.ScopeUnsupported(calendarId);

        Result supported = await RequireAsync(calendarId, CalendarCapabilities.Update, "updating", cancellationToken);
        if (supported.IsFailure)
            return supported;

        var @event = new JsonObject
        {
            ["summary"] = data.Summary,
            ["dtstart"] = data.Start.DateTime is { } start ? Iso(start) : Date(data.Start),
            ["dtend"] = data.End.DateTime is { } end ? Iso(end) : Date(data.End),
        };
        if (data.Description is not null)
            @event["description"] = data.Description;
        if (data.Location is not null)
            @event["location"] = data.Location;

        var command = new JsonObject { ["entity_id"] = calendarId, ["uid"] = eventId, ["event"] = @event };
        if (recurrenceId is not null)
            command["recurrence_id"] = recurrenceId;

        Result<JsonElement> updated = await commands.CommandAsync("calendar/event/update", command, cancellationToken);
        return updated.IsSuccess ? Result.Success() : CalendarErrors.Failed(updated.Error.Message);
    }

    private async Task<Result> RequireAsync(string calendarId, CalendarCapabilities capability, string change, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CalendarInfo>> calendars = await ListCalendarsAsync(cancellationToken);
        if (calendars.IsFailure)
            return calendars.Error;
        if (calendars.Value.FirstOrDefault(calendar => calendar.Id == calendarId) is not { } found)
            return CalendarErrors.NotFound(calendarId);

        return found.Capabilities.HasFlag(capability) ? Result.Success() : CalendarErrors.Unsupported(calendarId, change);
    }

    private CalendarCapabilities Capabilities(string entityId)
    {
        if (cache.Current?.States.GetValueOrDefault(entityId) is not { } state
            || state.Attributes.ValueKind != JsonValueKind.Object
            || !state.Attributes.TryGetProperty("supported_features", out JsonElement features)
            || !features.TryGetInt32(out int flags))
        {
            return CalendarCapabilities.None;
        }

        return ((flags & CreateEvent) != 0 ? CalendarCapabilities.Create : CalendarCapabilities.None)
            | ((flags & UpdateEvent) != 0 ? CalendarCapabilities.Update : CalendarCapabilities.None);
    }

    private static CalendarTime Time(JsonElement value, TimeZoneInfo zone)
    {
        if (Text(value, "dateTime") is { } dateTime)
            return CalendarTime.At(TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(dateTime, CultureInfo.InvariantCulture), zone));

        return CalendarTime.AllDay(DateOnly.ParseExact(Text(value, "date")!, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static string Iso(DateTimeOffset time) => time.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Date(CalendarTime time) =>
        (time.Date ?? DateOnly.FromDateTime(time.DateTime!.Value.DateTime)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex("^calendar\\.[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityId();
}
