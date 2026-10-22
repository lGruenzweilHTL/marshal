namespace Marshal.Integrations.CalDav.Tests;

/// <summary>iCalendar resources for the provider and poller tests.</summary>
internal static class Ics
{
    public const string Weekly = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:weekly-standup
        DTSTAMP:20261001T000000Z
        DTSTART;TZID=Europe/Vienna:20261019T090000
        DTEND;TZID=Europe/Vienna:20261019T100000
        RRULE:FREQ=WEEKLY;COUNT=4
        SUMMARY:Standup
        LOCATION:Room 2
        ORGANIZER;CN=Anna Huber:mailto:anna@example.org
        ATTENDEE;CN=Anna Huber;ROLE=CHAIR;PARTSTAT=ACCEPTED:mailto:anna@example.org
        ATTENDEE;CN=Ben;ROLE=REQ-PARTICIPANT;PARTSTAT=TENTATIVE:mailto:ben@example.org
        ATTENDEE;CN=Lukas;ROLE=OPT-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:Lukas@Example.org
        END:VEVENT
        END:VCALENDAR
        """;

    public const string WeeklyMoved = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:weekly-standup
        DTSTAMP:20261001T000000Z
        DTSTART;TZID=Europe/Vienna:20261019T090000
        DTEND;TZID=Europe/Vienna:20261019T100000
        RRULE:FREQ=WEEKLY;COUNT=4
        SUMMARY:Standup
        END:VEVENT
        BEGIN:VEVENT
        UID:weekly-standup
        DTSTAMP:20261001T000000Z
        RECURRENCE-ID;TZID=Europe/Vienna:20261026T090000
        DTSTART;TZID=Europe/Vienna:20261026T110000
        DTEND;TZID=Europe/Vienna:20261026T120000
        SUMMARY:Standup (moved)
        END:VEVENT
        END:VCALENDAR
        """;

    public const string Dentist = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:dentist
        DTSTAMP:20261001T000000Z
        DTSTART:20261022T070000Z
        DTEND:20261022T080000Z
        SUMMARY:Dentist
        DESCRIPTION:Bring the card
        END:VEVENT
        END:VCALENDAR
        """;

    public const string Holiday = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:holiday
        DTSTAMP:20261001T000000Z
        DTSTART;VALUE=DATE:20261026
        DTEND;VALUE=DATE:20261027
        SUMMARY:National holiday
        TRANSP:TRANSPARENT
        END:VEVENT
        END:VCALENDAR
        """;

    public const string Cancelled = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:cancelled
        DTSTAMP:20261001T000000Z
        DTSTART:20261021T070000Z
        DTEND:20261021T080000Z
        SUMMARY:Called off
        STATUS:CANCELLED
        END:VEVENT
        END:VCALENDAR
        """;

    /// <summary>An invitation: the account (lukas@example.org) is an attendee who has not answered.</summary>
    public const string Invitation = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        METHOD:REQUEST
        BEGIN:VEVENT
        UID:invite-1
        DTSTAMP:20261020T080000Z
        DTSTART:20261022T080000Z
        DTEND:20261022T090000Z
        SUMMARY:Planning
        ORGANIZER;CN=Anna:mailto:anna@example.org
        ATTENDEE;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:lukas@example.org
        END:VEVENT
        END:VCALENDAR
        """;
}
