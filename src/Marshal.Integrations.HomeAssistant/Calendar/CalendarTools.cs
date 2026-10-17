using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant.Calendar;

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
            }), _json),
            string.Join(',', ordered.Select(entry => $"{entry.CalendarId}/{entry.Id}")));
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
        Result updated = await context.Provider.UpdateEventAsync(calendar, Text(arguments, "eventId")!, Text(arguments, "recurrenceId"), EventData(arguments, zone), ct);
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

    private static Result ValidateEvent(JsonElement arguments) =>
        Text(arguments, "calendar") is null || Text(arguments, "summary") is null ? Invalid("calendar and summary are required.") : ValidateTimes(arguments);

    private static Result ValidateUpdate(JsonElement arguments) =>
        Text(arguments, "eventId") is null ? Invalid("eventId is required.") : ValidateEvent(arguments);

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

        public string Summarize(JsonElement arguments, string language) =>
            Verb is null
                ? $"{Descriptor.Id} {arguments.GetRawText()}"
                : $"{Verb} event \"{Text(arguments, "summary")}\" in {Text(arguments, "calendar")}: {Text(arguments, "start")} – {Text(arguments, "end")}";

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            JsonElement arguments = invocation.Arguments.ValueKind == JsonValueKind.Object ? invocation.Arguments : JsonDocument.Parse("{}").RootElement;
            Result valid = Validate(arguments);
            return valid.IsFailure ? valid.Error : await run(context, arguments, invocation.Context, cancellationToken);
        }
    }
}
