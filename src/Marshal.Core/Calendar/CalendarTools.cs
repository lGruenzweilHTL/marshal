using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Calendar;

/// <summary>
/// Model-facing calendar tools. Times without an offset are in the owner's time zone, and dates alone mean whole
/// days (end dates inclusive). Event content can come from third parties (invitations), so
/// <c>calendar.list_events</c> is untrusted (taint source <c>calendar</c>). Creating and updating events needs
/// confirmation; there is no delete.
/// </summary>
internal static class CalendarTools
{
    private const string Taint = "calendar";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    // Events leave out what their provider does not know (organizer, attendees, series) instead of sending nulls.
    private static readonly JsonSerializerOptions _eventJson = new(_json) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private const string TimeHelp = "Local time like 2026-09-30T09:00 (the user's time zone), or a date like 2026-09-30 for whole days.";

    public static IReadOnlyList<ITool> Create(ICalendarProvider provider, IUserProfileProvider profiles)
    {
        var context = new CalendarToolContext(provider, profiles);
        return
        [
            new CalendarTool(context, "list_calendars", "Lists the calendars and whether events can be created or changed in them.", """
                {"type":"object","properties":{}}
                """, RiskTier.Safe, untrusted: false, CalendarCapabilities.None, ListCalendarsAsync),
            new CalendarTool(context, "list_events", "Lists events between start and end, in every calendar or one.", $$$"""
                {"type":"object","properties":{
                  "calendar":{"type":"string","description":"Calendar id from calendar_list_calendars; all calendars when omitted."},
                  "start":{"type":"string","description":"{{{TimeHelp}}}"},
                  "end":{"type":"string","description":"{{{TimeHelp}}} A date includes the whole day."}},
                 "required":["start","end"]}
                """, RiskTier.Safe, untrusted: true, CalendarCapabilities.None, ListEventsAsync, ValidateRange),
            new CalendarTool(context, "find_free_time", "Finds free time slots of at least the given length between start and end, across every calendar or the ones named. Events marked free and meetings the user declined do not count as busy.", $$$"""
                {"type":"object","properties":{
                  "start":{"type":"string","description":"{{{TimeHelp}}}"},
                  "end":{"type":"string","description":"{{{TimeHelp}}} A date includes the whole day."},
                  "duration":{"type":"integer","minimum":1,"description":"Minimum length of a slot in minutes."},
                  "calendars":{"type":"array","items":{"type":"string"},"description":"Calendar ids from calendar_list_calendars; all calendars when omitted."},
                  "within":{"type":"string","description":"Daily hours to search, like 08:00-18:00."}},
                 "required":["start","end","duration"]}
                """, RiskTier.Safe, untrusted: false, CalendarCapabilities.None, FindFreeTimeAsync, ValidateFreeTime),
            new CalendarTool(context, "create_event", "Creates an event. The user confirms it first.", $$$"""
                {"type":"object","properties":{
                  "calendar":{"type":"string","description":"Calendar id from calendar_list_calendars."},
                  "summary":{"type":"string"},
                  "start":{"type":"string","description":"{{{TimeHelp}}}"},
                  "end":{"type":"string","description":"{{{TimeHelp}}} For whole days the last day of the event."},
                  "description":{"type":"string"},"location":{"type":"string"}},
                 "required":["calendar","summary","start","end"]}
                """, RiskTier.Confirm, untrusted: false, CalendarCapabilities.Create, CreateEventAsync, ValidateEvent)
            {
                Verb = "Create",
            },
            new CalendarTool(context, "update_event", "Replaces an event's title, times, description and location; pass every field, taking unchanged ones from calendar_list_events. The user confirms it first.", $$$"""
                {"type":"object","properties":{
                  "calendar":{"type":"string"},
                  "eventId":{"type":"string","description":"Event id from calendar_list_events."},
                  "recurrenceId":{"type":"string","description":"recurrenceId from calendar_list_events, to change one instance of a recurring event."},
                  "scope":{"type":"string","enum":["occurrence","series"],"description":"For a recurring event: occurrence changes only the instance named by recurrenceId (the default when one is given), series changes every instance. Only for calendars that can change single instances."},
                  "summary":{"type":"string"},
                  "start":{"type":"string","description":"{{{TimeHelp}}}"},
                  "end":{"type":"string","description":"{{{TimeHelp}}} For whole days the last day of the event."},
                  "description":{"type":"string"},"location":{"type":"string"}},
                 "required":["calendar","eventId","summary","start","end"]}
                """, RiskTier.Confirm, untrusted: false, CalendarCapabilities.Update, UpdateEventAsync, ValidateUpdate)
            {
                Verb = "Update",
            },
        ];
    }

    private static async Task<Result<ToolOutput>> ListCalendarsAsync(CalendarToolContext context, JsonElement arguments, MarshalExecutionContext run, CancellationToken ct) =>
        (await context.Provider.ListCalendarsAsync(ct)).Map(calendars => new ToolOutput(JsonSerializer.Serialize(calendars.Select(calendar => new
        {
            calendar.Id,
            calendar.Name,
            canCreate = calendar.Capabilities.HasFlag(CalendarCapabilities.Create),
            canUpdate = calendar.Capabilities.HasFlag(CalendarCapabilities.Update),
        }), _json)));

    private static async Task<Result<ToolOutput>> ListEventsAsync(CalendarToolContext context, JsonElement arguments, MarshalExecutionContext run, CancellationToken ct)
    {
        TimeZoneInfo zone = await context.ZoneAsync(run, ct);
        CalendarTime start = ParseTime(Text(arguments, "start"), zone)!;
        CalendarTime end = ParseTime(Text(arguments, "end"), zone)!;
        DateTimeOffset from = start.DateTime ?? DayStart(start.Date!.Value, zone);
        DateTimeOffset until = end.DateTime ?? DayStart(end.Date!.Value.AddDays(1), zone);

        IReadOnlyList<string> calendars;
        if (Text(arguments, "calendar") is { } one)
        {
            calendars = [one];
        }
        else
        {
            Result<IReadOnlyList<CalendarInfo>> all = await context.Provider.ListCalendarsAsync(ct);
            if (all.IsFailure)
                return all.Error;
            calendars = [.. all.Value.Select(calendar => calendar.Id)];
        }

        var events = new List<CalendarEvent>();
        foreach (string calendar in calendars)
        {
            Result<IReadOnlyList<CalendarEvent>> found = await context.Provider.ListEventsAsync(calendar, from, until, zone, ct);
            if (found.IsFailure)
                return found.Error;
            events.AddRange(found.Value);
        }

        List<CalendarEvent> ordered = [.. events.OrderBy(entry => entry.Start.DateTime ?? DayStart(entry.Start.Date!.Value, zone))];
        return new ToolOutput(
            JsonSerializer.Serialize(ordered.Select(entry => new
            {
                calendar = entry.CalendarId,
                entry.Id,
                entry.RecurrenceId,
                entry.Summary,
                start = Format(entry.Start, isEnd: false),
                end = Format(entry.End, isEnd: true),
                allDay = entry.Start.DateTime is null,
                entry.Description,
                entry.Location,
                organizer = entry.Organizer is { } organizer ? new { organizer.Name, organizer.Address } : null,
                attendees = entry.Attendees is { Count: > 0 } attendees
                    ? attendees.Select(attendee => new { attendee.Name, attendee.Address, role = attendee.Role.ToLowerInvariant(), status = Rsvp(attendee.Status) })
                    : null,
                recurring = entry.Recurring ? true : (bool?)null,
                entry.SeriesId,
                originalStart = entry.OriginalStart is { } original ? Format(CalendarTime.At(TimeZoneInfo.ConvertTime(original, zone)), isEnd: false) : null,
            }), _eventJson),
            string.Join(',', ordered.Select(entry => $"{entry.CalendarId}/{entry.Id}")));
    }

    private const int MaxFreeSlots = 20;

    // Busy are all events not marked free (TRANSP:TRANSPARENT) and not declined by the account; only times and calendar
    // ids leave the tool, so its output is trusted.
    private static async Task<Result<ToolOutput>> FindFreeTimeAsync(CalendarToolContext context, JsonElement arguments, MarshalExecutionContext run, CancellationToken ct)
    {
        TimeZoneInfo zone = await context.ZoneAsync(run, ct);
        CalendarTime start = ParseTime(Text(arguments, "start"), zone)!;
        CalendarTime end = ParseTime(Text(arguments, "end"), zone)!;
        DateTimeOffset from = start.DateTime ?? DayStart(start.Date!.Value, zone);
        DateTimeOffset until = end.DateTime ?? DayStart(end.Date!.Value.AddDays(1), zone);
        var duration = TimeSpan.FromMinutes(arguments.GetProperty("duration").GetInt32());

        IReadOnlyList<string> calendars;
        if (arguments.TryGetProperty("calendars", out JsonElement named))
        {
            calendars = [.. named.EnumerateArray().Select(item => item.GetString()!)];
        }
        else
        {
            Result<IReadOnlyList<CalendarInfo>> all = await context.Provider.ListCalendarsAsync(ct);
            if (all.IsFailure)
                return all.Error;
            calendars = [.. all.Value.Select(calendar => calendar.Id)];
        }

        List<(DateTimeOffset Start, DateTimeOffset End)> busy = [];
        foreach (string calendar in calendars)
        {
            Result<IReadOnlyList<CalendarEvent>> found = await context.Provider.ListEventsAsync(calendar, from, until, zone, ct);
            if (found.IsFailure)
                return found.Error;
            busy.AddRange(found.Value
                .Where(entry => !entry.Transparent && !(entry.Attendees ?? []).Any(attendee => attendee.IsAccount && attendee.Status == CalendarRsvp.Declined))
                .Select(entry => (entry.Start.DateTime ?? DayStart(entry.Start.Date!.Value, zone), entry.End.DateTime ?? DayStart(entry.End.Date!.Value, zone))));
        }

        List<(DateTimeOffset Start, DateTimeOffset End)> free = [];
        foreach ((DateTimeOffset windowStart, DateTimeOffset windowEnd) in Windows(from, until, ParseWithin(Text(arguments, "within")), zone))
        {
            DateTimeOffset cursor = windowStart;
            foreach ((DateTimeOffset busyStart, DateTimeOffset busyEnd) in busy.Where(item => item.End > windowStart && item.Start < windowEnd).OrderBy(item => item.Start))
            {
                if (busyStart - cursor >= duration)
                    free.Add((cursor, busyStart));
                if (busyEnd > cursor)
                    cursor = busyEnd;
            }

            if (windowEnd - cursor >= duration)
                free.Add((cursor, windowEnd));
        }

        return new ToolOutput(JsonSerializer.Serialize(new
        {
            calendars,
            free = free.Take(MaxFreeSlots).Select(slot => new { start = Format(CalendarTime.At(slot.Start), isEnd: false), end = Format(CalendarTime.At(slot.End), isEnd: true) }),
        }, _json));
    }

    // The search range, cut into daily windows when "within" names hours.
    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Windows(DateTimeOffset from, DateTimeOffset until, (TimeOnly Start, TimeOnly End)? within, TimeZoneInfo zone)
    {
        if (within is not { } hours)
        {
            yield return (from, until);
            yield break;
        }

        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(until, zone).DateTime);
        for (var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime); day <= last; day = day.AddDays(1))
        {
            DateTimeOffset start = Max(InZone(day.ToDateTime(hours.Start), zone), from);
            DateTimeOffset end = Min(InZone(day.ToDateTime(hours.End), zone), until);
            if (end > start)
                yield return (start, end);
        }
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static (TimeOnly Start, TimeOnly End)? ParseWithin(string? text)
    {
        if (text?.Split('-', StringSplitOptions.TrimEntries) is not [{ } first, { } second]
            || !TimeOnly.TryParseExact(first, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly start)
            || !TimeOnly.TryParseExact(second, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly end)
            || end <= start)
        {
            return null;
        }

        return (start, end);
    }

    private static async Task<Result<ToolOutput>> CreateEventAsync(CalendarToolContext context, JsonElement arguments, MarshalExecutionContext run, CancellationToken ct)
    {
        TimeZoneInfo zone = await context.ZoneAsync(run, ct);
        string calendar = Text(arguments, "calendar")!;
        Result created = await context.Provider.CreateEventAsync(calendar, EventData(arguments, zone), ct);
        return created.IsSuccess ? new ToolOutput($"Created \"{Text(arguments, "summary")}\" in {calendar}.") : created.Error;
    }

    private static async Task<Result<ToolOutput>> UpdateEventAsync(CalendarToolContext context, JsonElement arguments, MarshalExecutionContext run, CancellationToken ct)
    {
        TimeZoneInfo zone = await context.ZoneAsync(run, ct);
        string calendar = Text(arguments, "calendar")!;
        CalendarUpdateScope? scope = Scope(Text(arguments, "scope"));
        if (scope is not null)
        {
            Result<IReadOnlyList<CalendarInfo>> calendars = await context.Provider.ListCalendarsAsync(ct);
            if (calendars.IsFailure)
                return calendars.Error;
            if (calendars.Value.FirstOrDefault(info => info.Id == calendar) is { } info && !info.Capabilities.HasFlag(CalendarCapabilities.UpdateOccurrence))
                return CalendarErrors.ScopeUnsupported(calendar);
        }

        Result updated = await context.Provider.UpdateEventAsync(calendar, Text(arguments, "eventId")!, Text(arguments, "recurrenceId"), EventData(arguments, zone), scope, ct);
        return updated.IsSuccess ? new ToolOutput($"Updated \"{Text(arguments, "summary")}\" in {calendar}.") : updated.Error;
    }

    private static CalendarEventData EventData(JsonElement arguments, TimeZoneInfo zone)
    {
        CalendarTime start = ParseTime(Text(arguments, "start"), zone)!;
        CalendarTime end = ParseTime(Text(arguments, "end"), zone)!;
        // Whole days: the model names the last day, the provider takes the exclusive end.
        if (end.Date is { } lastDay)
            end = CalendarTime.AllDay(lastDay.AddDays(1));

        return new CalendarEventData(Text(arguments, "summary")!, start, end, Text(arguments, "description"), Text(arguments, "location"));
    }

    private static Result ValidateRange(JsonElement arguments) => ValidateTimes(arguments);

    private static Result ValidateFreeTime(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("duration", out JsonElement duration) || !duration.TryGetInt32(out int minutes) || minutes < 1)
            return Invalid("duration must be a whole number of minutes, at least 1.");
        if (arguments.TryGetProperty("calendars", out JsonElement calendars)
            && (calendars.ValueKind != JsonValueKind.Array || calendars.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))))
        {
            return Invalid("calendars must be a list of calendar ids.");
        }

        if (Text(arguments, "within") is { } within && ParseWithin(within) is null)
            return Invalid("within must be daily hours like 08:00-18:00, the end after the start.");
        return ValidateTimes(arguments);
    }

    private static Result ValidateEvent(JsonElement arguments) =>
        Text(arguments, "calendar") is null || Text(arguments, "summary") is null ? Invalid("calendar and summary are required.") : ValidateTimes(arguments);

    private static Result ValidateUpdate(JsonElement arguments)
    {
        if (Text(arguments, "eventId") is null)
            return Invalid("eventId is required.");
        if (Text(arguments, "scope") is { } scope && Scope(scope) is null)
            return Invalid($"scope must be occurrence or series, not '{scope}'.");
        if (Scope(Text(arguments, "scope")) == CalendarUpdateScope.Occurrence && Text(arguments, "recurrenceId") is null)
            return Invalid("scope occurrence needs the recurrenceId from calendar_list_events.");
        return ValidateEvent(arguments);
    }

    private static CalendarUpdateScope? Scope(string? text) => text switch
    {
        "occurrence" => CalendarUpdateScope.Occurrence,
        "series" => CalendarUpdateScope.Series,
        _ => null,
    };

    private static string Rsvp(CalendarRsvp status) => status switch
    {
        CalendarRsvp.Accepted => "accepted",
        CalendarRsvp.Declined => "declined",
        CalendarRsvp.Tentative => "tentative",
        CalendarRsvp.Delegated => "delegated",
        _ => "needs-action",
    };

    // Parsed in UTC only to check the shape and order; the owner's zone applies when the call runs.
    private static Result ValidateTimes(JsonElement arguments)
    {
        if (ParseTime(Text(arguments, "start"), TimeZoneInfo.Utc) is not { } start || ParseTime(Text(arguments, "end"), TimeZoneInfo.Utc) is not { } end)
            return Invalid($"start and end must be local times or dates. {TimeHelp}");
        if ((start.Date is null) != (end.Date is null))
            return Invalid("start and end must both be times or both be dates.");

        bool ordered = start.Date is { } firstDay ? end.Date >= firstDay : end.DateTime > start.DateTime;
        return ordered ? Result.Success() : Invalid("end must be after start.");
    }

    private static CalendarTime? ParseTime(string? text, TimeZoneInfo zone)
    {
        if (text is null)
            return null;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            return CalendarTime.AllDay(date);
        if (text.Length < 16 || text[4] != '-' || text[10] is not ('T' or ' '))
            return null;
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            return null;

        return parsed.Kind == DateTimeKind.Unspecified
            ? CalendarTime.At(InZone(parsed, zone))
            : CalendarTime.At(DateTimeOffset.Parse(text, CultureInfo.InvariantCulture));
    }

    // A wall-clock time in the zone: a time skipped by the DST change moves forward, a repeated one takes the first.
    private static DateTimeOffset InZone(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local))
            local = local.AddHours(1);

        TimeSpan offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static DateTimeOffset DayStart(DateOnly date, TimeZoneInfo zone) => InZone(date.ToDateTime(TimeOnly.MinValue), zone);

    private static string Format(CalendarTime time, bool isEnd) =>
        time.DateTime is { } at
            ? at.ToString("yyyy-MM-dd'T'HH:mmzzz", CultureInfo.InvariantCulture)
            : (isEnd ? time.Date!.Value.AddDays(-1) : time.Date!.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;

    private static Error Invalid(string message) => new("calendar.invalid_arguments", message, ErrorType.Validation);

    private sealed class CalendarToolContext(ICalendarProvider provider, IUserProfileProvider profiles)
    {
        public ICalendarProvider Provider => provider;

        /// <summary>The owner's time zone; UTC when the profile or zone cannot be read.</summary>
        public async Task<TimeZoneInfo> ZoneAsync(MarshalExecutionContext run, CancellationToken ct)
        {
            Result<UserProfile> profile = await profiles.GetAsync(run.OwnerId, ct);
            return profile.IsSuccess && TimeZoneInfo.TryFindSystemTimeZoneById(profile.Value.TimeZone, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Utc;
        }
    }

    private sealed class CalendarTool(
        CalendarToolContext context,
        string name,
        string description,
        string schema,
        RiskTier tier,
        bool untrusted,
        CalendarCapabilities requires,
        Func<CalendarToolContext, JsonElement, MarshalExecutionContext, CancellationToken, Task<Result<ToolOutput>>> run,
        Func<JsonElement, Result>? validate = null) : ITool
    {
        /// <summary>Verb of the confirmation summary for changing tools.</summary>
        public string? Verb { get; init; }

        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("calendar", name),
            Description = description,
            ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
            DefaultTier = tier,
            UntrustedOutput = untrusted,
            TaintKind = untrusted ? Taint : null,
            Fillers = name == "list_events" ? ToolFillers.Lines("Ich sehe im Kalender nach.", "Let me check the calendar.") : ToolFillers.None,
        };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext executionContext, CancellationToken cancellationToken)
        {
            if (!context.Provider.IsConfigured)
                return ValueTask.FromResult(false);

            CalendarOverview overview = context.Provider.Overview;
            return ValueTask.FromResult(overview.Count > 0 && overview.Capabilities.HasFlag(requires));
        }

        public Result Validate(JsonElement arguments) => validate?.Invoke(arguments) ?? Result.Success();

        public string Summarize(JsonElement arguments, string language)
        {
            if (Verb is null)
                return $"{Descriptor.Id} {arguments.GetRawText()}";

            string summary = $"{Verb} event \"{Text(arguments, "summary")}\" in {Text(arguments, "calendar")}: {Text(arguments, "start")} – {Text(arguments, "end")}";
            if (Text(arguments, "scope") is { } scope)
                summary += $" ({scope})";
            // A server that knows the attendees may send them the change; the user should know before confirming.
            if (Verb == "Update" && context.Provider.Overview.Capabilities.HasFlag(CalendarCapabilities.Attendees))
                summary += ". The calendar server may notify the event's attendees, if any";
            return summary;
        }

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            JsonElement arguments = invocation.Arguments.ValueKind == JsonValueKind.Object ? invocation.Arguments : JsonDocument.Parse("{}").RootElement;
            Result valid = Validate(arguments);
            return valid.IsFailure ? valid.Error : await run(context, arguments, invocation.Context, cancellationToken);
        }
    }
}
