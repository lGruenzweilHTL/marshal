# Proposal

## Why

Calendars reach Marshal only through Home Assistant's `calendar.*` entities, which carry summary, times, description and location, but no attendees, no RSVP state and limited recurrence handling. HA also polls on its own schedule, so Marshal hears nothing when an invitation arrives or a meeting moves. "Who's coming on Thursday?", "Did Anna accept?", "When do I have two free hours this week?" and "move only next week's occurrence" have no good answer today. The design notes planned native providers behind `ICalendarProvider` for exactly this.

## What Changes

- New CalDAV provider next to the HA provider. Each calendar comes from one of them; the tools stay the same and address calendars by id (HA entity ids as today, `caldav:<account>/<calendar>` for CalDAV).
- CalDAV accounts in `marshal.yaml` (`calendar.caldav.accounts`): server URL, user name, password or app password by secret name. Works with Nextcloud, Radicale, Baïkal, Fastmail, iCloud (app-specific password) and similar servers.
- Events from CalDAV carry the organizer, attendees (name, address, role, RSVP status) and recurrence information; `calendar.list_events` returns them where the provider has them.
- New safe tool `calendar.find_free_time(start, end, duration, calendars?, within?)`: free slots across calendars of both providers, computed from their events (recurrences expanded), optionally limited to daily hours.
- `calendar.update_event` gains `scope: occurrence | series` for recurring events (CalDAV; confirm tier as today).
- Change events: Marshal polls each CalDAV calendar with sync tokens and publishes `calendar.event_created`, `calendar.event_updated`, `calendar.event_removed` and `calendar.invitation` (a new event where the account is an attendee with RSVP `needs-action`) on the event bus. Payloads carry only ids and times, so watches can react without reading third-party text.

## Capabilities

### New Capabilities

None. CalDAV is a provider of the existing calendar capability.

### Modified Capabilities

- `calendar`: second provider and coexistence, CalDAV configuration, attendee and recurrence fields, `calendar.find_free_time`, update scope, change events.

## Non-goals

- Replying to invitations (RSVP). It sends data to third parties, like sending mail.
- Deleting events.
- OAuth providers (Google Calendar API, Microsoft Graph) and OAuth for CalDAV; Google and Microsoft calendars stay available through HA.
- Server-side free/busy queries (`free-busy-query` REPORT); free time is computed from events.
- Change events for HA calendars.

## Impact

- New project `src/Marshal.Integrations.CalDav` (CalDAV client, iCalendar parsing, provider, sync poller) with tests; repository map in `CLAUDE.md`; `packages.puml`.
- New package `Ical.Net` for iCalendar parsing and recurrence expansion (package index).
- `src/Marshal.Abstractions`: calendar event fields (organizer, attendees, recurrence), update scope.
- `src/Marshal.Core` or the calendar tool source: a composite provider over HA and CalDAV, `calendar.find_free_time`.
- `src/Marshal.Data`: sync state per CalDAV calendar; migration; `erd.puml`.
- `Marshal.Demo`: simulated attendees and a simulated invitation event.
- Docs: calendar guide (CalDAV setup per server, what HA vs. CalDAV offers), configuration reference.
