# Design

## Context

See proposal.md (Why). Current state:

- `ICalendarProvider` (`Marshal.Abstractions/Calendar`) has `IsConfigured`, `Overview`, `ListCalendarsAsync`, `ListEventsAsync` (and create / update). The HA implementation lives in `Marshal.Integrations.HomeAssistant/Calendar`; the calendar tools are built on the interface.
- `ICredentialSource` is mail-specific (`MailAccount`). CalDAV v1 needs only a password from the secret store.
- The event bus carries normalized events; the trigger matcher filters by `source`, `type` and fields; the Seerr watch example shows the pattern of id-only trusted payloads.

## Goals / Non-Goals

**Goals:**
- One set of calendar tools over both providers.
- Change events that let watches react without reading third-party text.

**Non-Goals:**
- Two-way sync or a local calendar cache beyond sync state.

## Decisions

### 1. Composite provider

`CompositeCalendarProvider` (`Marshal.Core`) implements `ICalendarProvider` over an ordered list of providers (HA, CalDAV) and routes by calendar id (`caldav:` prefix → CalDAV, else HA). The tools keep depending on the single interface. `CalendarInfo` gains capability flags (`CanCreate`, `CanUpdate`, `CanUpdateOccurrence`, `HasAttendees`); the tool visibility of `update_event` and validation of `scope` read them.

### 2. CalDAV client

`src/Marshal.Integrations.CalDav`:
- `ICalDavClient`: `DiscoverAsync(account)` (PROPFIND `current-user-principal` → `calendar-home-set` → calendar collections), `QueryAsync(calendar, start, end)` (REPORT `calendar-query` with `time-range`), `SyncAsync(calendar, token)` (REPORT `sync-collection`, RFC 6578), `PutAsync` / `GetAsync` with `If-Match` on ETags. v1: `HttpCalDavClient` (named `HttpClient`, Basic auth with the secret read per request, resilience).
- `CalDavCalendarProvider : ICalendarProvider`.
- iCalendar parsing, recurrence expansion and serialization with `Ical.Net` (MIT). Occurrence edits write a `RECURRENCE-ID` override VEVENT into the same resource. Series edits change the master and keep existing overrides whose times still fit.

Alternative: a CalDAV library. None of the .NET options is maintained enough; the protocol subset needed is small (PROPFIND, two REPORTs, GET, PUT) and testable against recorded responses and a Radicale Testcontainer.

### 3. Free time

`calendar.find_free_time` runs in the tool source over `ICalendarProvider.ListEventsAsync` for each selected calendar, merges busy intervals (skipping `TRANSP:TRANSPARENT` and events the account declined), subtracts them from `[start, end)` clipped to `within`, and returns intervals of at least `duration`. Because it only uses listed events, it works for HA calendars too. Its output holds times and calendar ids only, so it is trusted.

| Tool | Tier | Hidden when |
|---|---|---|
| `calendar.find_free_time(start, end, duration, calendars?, within?)` | safe | no calendar configured |
| `calendar.update_event(..., scope?)` | confirm (unchanged) | no calendar can update |

### 4. Change events

`CalDavSyncPoller` (hosted service): per calendar a timer (`pollMinutes`), `sync-collection` with the stored token; on `valid-sync-token` errors a full resync without publishing. Fallback without sync support: ETag map comparison. State in `caldav_sync_state (owner_id, account, calendar, sync_token, etag_map_json, last_poll)`. Published events: `{ source: calendar, type: event_created | event_updated | event_removed | invitation, entity: <calendar id>, payload: { eventId, start, end } }`. Invitation detection: new resource where an `ATTENDEE` equals the account's address (from the principal's `calendar-user-address-set`) with `PARTSTAT=NEEDS-ACTION`.

Accounts belong to the instance owner (configuration is instance-level), like mail accounts.

## Risks / Trade-offs

- [CalDAV servers differ (iCloud quirks, missing sync support)] → ETag fallback; recorded responses from Nextcloud, Radicale and iCloud in tests; a Radicale Testcontainer for the integration tests.
- [Recurrence edge cases (time zones, EXDATE, overrides)] → `Ical.Net` handles expansion; tests cover DST and overrides.
- [Polling load with many calendars] → `pollMinutes` per account; sync-collection responses are small when nothing changed.
- [Writing to a shared calendar triggers invitations sent by the server] → creating or updating events with attendees can make the server send notifications; the confirm text says so when attendees exist. Creating events with attendees stays out of v1 (the tools have no attendee argument).

## Migration Plan

EF migration adds `caldav_sync_state`. No change for users without `calendar.caldav`; calendar ids of HA calendars stay the same.

## Implementation notes

- Fixtures: the Radicale responses in `tests/Marshal.Integrations.CalDav.Tests/Fixtures/radicale/` are recorded from a real container. The Nextcloud and iCloud fixtures are hand-written from the servers' documented behavior (no accounts were at hand); the Radicale Testcontainer test covers the real protocol path.
- `CalendarInfo` capabilities are one flags enum (`Create`, `Update`, `UpdateOccurrence`, `Attendees`); sources implement `ICalendarSource` (adds `Owns(calendarId)`), and `CompositeCalendarProvider` and the calendar tools live in `Marshal.Core/Calendar`.
- The CalDAV password is read from the secret store per request (no `[Secret]` binding at startup), so a missing or rotated secret only affects that account and needs no restart.
- Series edits shift every occurrence by the move of the named occurrence and drop overrides when the series moves; with unchanged times overrides stay.
- The poller keeps `{ etag, uid }` per resource href in `etag_map_json` in both modes, so `event_removed` can name the event. Resources that existed before the first poll were never read; their removal uses the file name as event id.
- `invitation` is published together with `event_created`.
- Event uids are third-party text; only uids matching `^[A-Za-z0-9@._:+-]{1,200}$` go into the trusted payload, otherwise `eventId` is null.
- The poller and its event source are registered only when accounts are configured. Demo mode simulates a `caldav:demo/work` calendar (`DemoCalDavCalendars`) and publishes an invitation two minutes after start (`DemoCalDavEventSource`); `simulatedIntegrations` gains `caldav`.
- The `update_event` confirmation text notes possible attendee notifications whenever any calendar has attendee data (the summary cannot read the event without an async call).
