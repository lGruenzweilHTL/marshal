using System.Globalization;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using IcsCalendar = Ical.Net.Calendar;
using IcsEvent = Ical.Net.CalendarComponents.CalendarEvent;
using MarshalEvent = Marshal.Abstractions.Calendar.CalendarEvent;

namespace Marshal.Integrations.CalDav;

/// <summary>
/// iCalendar resources to and from calendar events with Ical.Net: recurrence expansion (overrides by
/// <c>RECURRENCE-ID</c> replace their occurrence), attendee mapping, new resources and occurrence or series edits.
/// Recurrence ids are the occurrence's original start in UTC (<c>yyyyMMddTHHmmssZ</c>) or its date for all-day series.
/// </summary>
internal static class IcsEvents
{
    private const string ProductId = "-//Marshal//CalDAV//EN";

    // Occurrences that started this long before the range can still overlap it.
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(31);

    public static IcsCalendar? Load(string data)
    {
        try
        {
            return IcsCalendar.Load(data);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException or System.Runtime.Serialization.SerializationException)
        {
            return null;
        }
    }

    /// <summary>The occurrences overlapping [<paramref name="start"/>, <paramref name="end"/>), times in <paramref name="zone"/>; cancelled ones are left out.</summary>
    public static IEnumerable<MarshalEvent> Occurrences(IcsCalendar calendar, string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, IReadOnlyCollection<string> accountAddresses)
    {
        HashSet<string> recurringUids = [.. calendar.Events.Where(item => IsSeries(item) || item.RecurrenceIdentifier is not null).Select(item => item.Uid ?? "")];
        var from = new CalDateTime((start - Lookback).UtcDateTime, CalDateTime.UtcTzId);
        foreach (Occurrence occurrence in calendar.GetOccurrences<IcsEvent>(from).TakeWhile(item => Instant(item.Period.StartTime, zone) < end))
        {
            var source = (IcsEvent)occurrence.Source;
            CalDateTime occurrenceStart = occurrence.Period.StartTime;
            CalDateTime occurrenceEnd = occurrence.Period.EffectiveEndTime ?? occurrenceStart;
            if (Instant(occurrenceEnd, zone) <= start && !(occurrenceEnd == occurrenceStart && Instant(occurrenceStart, zone) >= start))
                continue;
            if (string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                continue;

            bool recurring = recurringUids.Contains(source.Uid ?? "");
            CalDateTime? original = source.RecurrenceIdentifier?.StartTime ?? (recurring ? occurrenceStart : null);
            yield return new MarshalEvent(calendarId, source.Uid, original is null ? null : RecurrenceId(original), source.Summary ?? "", Time(occurrenceStart, zone), Time(occurrenceEnd, zone), source.Description, source.Location)
            {
                Organizer = source.Organizer?.Value is { } organizer ? new CalendarPerson(source.Organizer.CommonName, Address(organizer)) : null,
                Attendees = [.. source.Attendees.Where(attendee => attendee.Value is not null).Select(attendee => Attendee(attendee, accountAddresses))],
                Recurring = recurring,
                SeriesId = recurring ? source.Uid : null,
                OriginalStart = original is { HasTime: true } ? Instant(original, zone) : null,
                Transparent = string.Equals(source.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase),
            };
        }
    }

    /// <summary>A new resource with one event.</summary>
    public static string Create(string uid, CalendarEventData data, DateTimeOffset now)
    {
        var calendar = new IcsCalendar { ProductId = ProductId };
        var item = new IcsEvent { Uid = uid, DtStamp = Utc(now), Created = Utc(now) };
        Apply(item, data);
        calendar.Events.Add(item);
        return Serialize(calendar);
    }

    /// <summary>
    /// Applies an update to a resource. A recurrence id with scope <see cref="CalendarUpdateScope.Occurrence"/> (the
    /// default for one) writes or replaces that occurrence's override; scope <see cref="CalendarUpdateScope.Series"/>
    /// changes the series and shifts it by the move of the named occurrence, dropping overrides that no longer fit.
    /// </summary>
    public static Result<string> Update(IcsCalendar calendar, string calendarId, string uid, string? recurrenceId, CalendarEventData data, CalendarUpdateScope? scope, DateTimeOffset now)
    {
        List<IcsEvent> components = [.. calendar.Events.Where(item => item.Uid == uid)];
        IcsEvent? master = components.FirstOrDefault(item => item.RecurrenceIdentifier is null);
        if (master is null)
            return CalendarErrors.Invalid($"Event '{uid}' has no series in calendar '{calendarId}'.");

        bool series = IsSeries(master);
        CalendarUpdateScope effective = scope ?? (recurrenceId is not null && series ? CalendarUpdateScope.Occurrence : CalendarUpdateScope.Series);
        if (effective == CalendarUpdateScope.Occurrence)
        {
            if (!series)
                return CalendarErrors.Invalid("scope occurrence needs a recurring event; this event does not repeat.");
            if (recurrenceId is null || ParseRecurrenceId(recurrenceId) is not { } original)
                return CalendarErrors.Invalid("scope occurrence needs the recurrenceId from calendar_list_events.");

            IcsEvent? existing = components.FirstOrDefault(item => item.RecurrenceIdentifier is { } id && SameInstant(id.StartTime, original));
            IcsEvent target = existing ?? NewOverride(master, original);
            if (existing is null)
                calendar.Events.Add(target);

            Apply(target, data);
            Touch(target, now);
            return Serialize(calendar);
        }

        if (series && recurrenceId is not null && ParseRecurrenceId(recurrenceId) is { } named && data.Start.DateTime is { } newStart && master.DtStart is { HasTime: true } masterStart)
        {
            // Moving one occurrence of the series moves all of them by the same amount; the duration comes from the data.
            TimeSpan shift = newStart.UtcDateTime - named.UtcDateTime;
            CalendarEventData shifted = data with
            {
                Start = CalendarTime.At(new DateTimeOffset(masterStart.AsUtc, TimeSpan.Zero) + shift),
                End = CalendarTime.At(new DateTimeOffset(masterStart.AsUtc, TimeSpan.Zero) + shift + (data.End.DateTime!.Value - newStart)),
            };
            ApplySeries(master, shifted, keepZone: true);
            if (shift != TimeSpan.Zero)
                DropOverrides(calendar, uid);
        }
        else
        {
            bool moved = series && master.DtStart is { } before && data.Start.DateTime is { } after && !SameInstant(before, after.UtcDateTime);
            ApplySeries(master, data, keepZone: series);
            if (moved)
                DropOverrides(calendar, uid);
        }

        Touch(master, now);
        return Serialize(calendar);
    }

    public static string Serialize(IcsCalendar calendar) => new CalendarSerializer().SerializeToString(calendar)!;

    /// <summary>Whether the account is an attendee who has not answered yet (an invitation).</summary>
    public static bool IsInvitation(IcsCalendar calendar, IReadOnlyCollection<string> accountAddresses) =>
        calendar.Events.Any(item => item.Attendees.Any(attendee =>
            attendee.Value is not null
            && IsAccount(Address(attendee.Value), accountAddresses)
            && string.Equals(attendee.ParticipationStatus, "NEEDS-ACTION", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The first event's uid, start and end (UTC) for change events; null when the resource holds no event.</summary>
    public static (string Uid, DateTimeOffset? Start, DateTimeOffset? End)? Summary(IcsCalendar calendar)
    {
        IcsEvent? master = calendar.Events.FirstOrDefault(item => item.RecurrenceIdentifier is null) ?? calendar.Events.FirstOrDefault();
        if (master?.Uid is not { } uid)
            return null;

        DateTimeOffset? start = master.DtStart is { } dtStart ? Instant(dtStart, TimeZoneInfo.Utc) : null;
        DateTimeOffset? end = master.DtEnd is { } dtEnd ? Instant(dtEnd, TimeZoneInfo.Utc) : start is { } begin && master.Duration is { } duration ? begin + duration.ToTimeSpanUnspecified() : null;
        return (uid, start, end);
    }

    private static bool IsSeries(IcsEvent item) =>
#pragma warning disable CS0618 // RecurrenceRules: servers still send several rules; the singular property covers only one.
        item.RecurrenceRules.Count > 0 || item.RecurrenceDates.GetAllPeriods().Any();
#pragma warning restore CS0618

    private static IcsEvent NewOverride(IcsEvent master, DateTimeOffset original)
    {
        CalDateTime start = master.DtStart!;
        CalDateTime id = start.HasTime
            ? start.TzId is { } zone && zone != CalDateTime.UtcTzId ? new CalDateTime(original.UtcDateTime, CalDateTime.UtcTzId).ToTimeZone(zone) : new CalDateTime(original.UtcDateTime, CalDateTime.UtcTzId)
            : new CalDateTime(DateOnly.FromDateTime(original.UtcDateTime));
        var item = new IcsEvent
        {
            Uid = master.Uid,
            RecurrenceIdentifier = new RecurrenceIdentifier(id, null),
            Organizer = master.Organizer,
            Sequence = master.Sequence,
        };
        foreach (Attendee attendee in master.Attendees)
            item.Attendees.Add(attendee);
        return item;
    }

    private static void DropOverrides(IcsCalendar calendar, string uid)
    {
        // Remove compares events by value, and an override equals its master in Ical.Net, so rebuild the list instead.
        List<IcsEvent> kept = [.. calendar.Events.Where(item => item.Uid != uid || item.RecurrenceIdentifier is null)];
        calendar.Events.Clear();
        foreach (IcsEvent item in kept)
            calendar.Events.Add(item);
    }

    private static void Apply(IcsEvent item, CalendarEventData data)
    {
        item.Summary = data.Summary;
        item.Description = data.Description;
        item.Location = data.Location;
        // Ical.Net refuses DURATION next to DTEND, even to clear it; events read from a server carry one or the other.
        if (item.Duration is not null)
            item.Duration = null;
        item.DtStart = Value(data.Start);
        item.DtEnd = Value(data.End);
    }

    // A series keeps its time zone, so it keeps its wall-clock time across DST changes.
    private static void ApplySeries(IcsEvent master, CalendarEventData data, bool keepZone)
    {
        string? zone = master.DtStart?.TzId;
        Apply(master, data);
        if (keepZone && zone is not null && zone != CalDateTime.UtcTzId && data.Start.DateTime is not null)
        {
            master.DtStart = master.DtStart!.ToTimeZone(zone);
            master.DtEnd = master.DtEnd!.ToTimeZone(zone);
        }
    }

    private static void Touch(IcsEvent item, DateTimeOffset now)
    {
        item.DtStamp = Utc(now);
        item.LastModified = Utc(now);
        item.Sequence++;
    }

    private static CalDateTime Value(CalendarTime time) =>
        time.DateTime is { } at ? new CalDateTime(at.UtcDateTime, CalDateTime.UtcTzId) : new CalDateTime(time.Date!.Value);

    private static CalDateTime Utc(DateTimeOffset time) => new(time.UtcDateTime, CalDateTime.UtcTzId);

    private static CalendarTime Time(CalDateTime value, TimeZoneInfo zone) =>
        value.HasTime ? CalendarTime.At(Instant(value, zone)) : CalendarTime.AllDay(value.Date);

    /// <summary>The instant in <paramref name="zone"/>; floating times and dates are read as local time there.</summary>
    private static DateTimeOffset Instant(CalDateTime value, TimeZoneInfo zone)
    {
        if (value.IsFloating)
        {
            DateTime local = value.HasTime ? value.Value : value.Date.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        return TimeZoneInfo.ConvertTime(new DateTimeOffset(value.AsUtc, TimeSpan.Zero), zone);
    }

    private static string RecurrenceId(CalDateTime original) =>
        original.HasTime
            ? original.AsUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
            : original.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseRecurrenceId(string text)
    {
        if (DateTime.TryParseExact(text, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime utc))
            return new DateTimeOffset(utc, TimeSpan.Zero);
        if (DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime date))
            return new DateTimeOffset(date, TimeSpan.Zero);
        return null;
    }

    private static bool SameInstant(CalDateTime value, DateTimeOffset instant) =>
        value.HasTime ? value.AsUtc == instant.UtcDateTime : value.Date == DateOnly.FromDateTime(instant.UtcDateTime);

    private static bool SameInstant(CalDateTime value, DateTime utc) => SameInstant(value, new DateTimeOffset(utc, TimeSpan.Zero));

    private static CalendarAttendee Attendee(Attendee attendee, IReadOnlyCollection<string> accountAddresses)
    {
        string address = Address(attendee.Value!);
        return new CalendarAttendee(attendee.CommonName, address, attendee.Role ?? "REQ-PARTICIPANT", Rsvp(attendee.ParticipationStatus), IsAccount(address, accountAddresses));
    }

    private static CalendarRsvp Rsvp(string? status) => status?.ToUpperInvariant() switch
    {
        "ACCEPTED" => CalendarRsvp.Accepted,
        "DECLINED" => CalendarRsvp.Declined,
        "TENTATIVE" => CalendarRsvp.Tentative,
        "DELEGATED" => CalendarRsvp.Delegated,
        _ => CalendarRsvp.NeedsAction,
    };

    private static bool IsAccount(string address, IReadOnlyCollection<string> accountAddresses) =>
        accountAddresses.Any(own => string.Equals(own, address, StringComparison.OrdinalIgnoreCase));

    private static string Address(Uri value) =>
        value.Scheme == Uri.UriSchemeMailto ? Uri.UnescapeDataString(value.OriginalString["mailto:".Length..]) : value.OriginalString;
}
