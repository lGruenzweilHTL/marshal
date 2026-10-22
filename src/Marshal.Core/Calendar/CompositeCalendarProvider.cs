using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Microsoft.Extensions.Logging;

namespace Marshal.Core.Calendar;

/// <summary>
/// The one <see cref="ICalendarProvider"/> the calendar tools see: the configured <see cref="ICalendarSource"/>s (Home
/// Assistant, CalDAV) side by side, routed by calendar id. A source that fails to list its calendars is left out with
/// a logged warning, so the others keep working; its own health warning names the cause.
/// </summary>
public sealed partial class CompositeCalendarProvider(IEnumerable<ICalendarSource> sources, ILogger<CompositeCalendarProvider> logger) : ICalendarProvider
{
    private readonly IReadOnlyList<ICalendarSource> _sources = [.. sources];

    private IEnumerable<ICalendarSource> Configured => _sources.Where(source => source.IsConfigured);

    public bool IsConfigured => Configured.Any();

    public CalendarOverview Overview
    {
        get
        {
            List<CalendarOverview> overviews = [.. Configured.Select(source => source.Overview)];
            return new CalendarOverview(overviews.Sum(overview => overview.Count), overviews.Aggregate(CalendarCapabilities.None, (all, overview) => all | overview.Capabilities));
        }
    }

    public async Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken)
    {
        var calendars = new List<CalendarInfo>();
        Error? firstFailure = null;
        int listed = 0;
        foreach (ICalendarSource source in Configured)
        {
            Result<IReadOnlyList<CalendarInfo>> found = await source.ListCalendarsAsync(cancellationToken);
            if (found.IsFailure)
            {
                LogSourceFailed(logger, source.GetType().Name, found.Error.Message);
                firstFailure ??= found.Error;
                continue;
            }

            listed++;
            calendars.AddRange(found.Value);
        }

        return listed == 0 && firstFailure is { } failure ? failure : calendars;
    }

    public Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken) =>
        Find(calendarId) is { } source
            ? source.ListEventsAsync(calendarId, start, end, zone, cancellationToken)
            : Task.FromResult<Result<IReadOnlyList<CalendarEvent>>>(CalendarErrors.NotFound(calendarId));

    public Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken) =>
        Find(calendarId) is { } source
            ? source.CreateEventAsync(calendarId, data, cancellationToken)
            : Task.FromResult<Result>(CalendarErrors.NotFound(calendarId));

    public Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CalendarUpdateScope? scope, CancellationToken cancellationToken) =>
        Find(calendarId) is { } source
            ? source.UpdateEventAsync(calendarId, eventId, recurrenceId, data, scope, cancellationToken)
            : Task.FromResult<Result>(CalendarErrors.NotFound(calendarId));

    private ICalendarSource? Find(string calendarId) => Configured.FirstOrDefault(source => source.Owns(calendarId));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Calendar source {Source} could not list its calendars: {Reason}")]
    private static partial void LogSourceFailed(ILogger logger, string source, string reason);
}
