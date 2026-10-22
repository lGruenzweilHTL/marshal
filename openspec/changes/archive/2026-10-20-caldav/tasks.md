# Tasks

## 1. Project, package and configuration

- [x] 1.1 Create `src/Marshal.Integrations.CalDav` and `tests/Marshal.Integrations.CalDav.Tests`, add them to `Marshal.sln`, reference from `Marshal.Api`; add `Ical.Net` to `Directory.Packages.props` and the `CLAUDE.md` package index; update the repository map and `packages.puml` (re-render the SVG). Verify the solution builds.
- [x] 1.2 Write failing options tests for `calendar.caldav.accounts` (id pattern, URL rules, secret name, `calendars` list, `pollMinutes` 1–60); implement with validation. Verify the tests pass.

## 2. Abstractions and composite provider

- [x] 2.1 Write failing tests: `CalendarEvent` carries optional organizer, attendees, recurrence fields; `CalendarInfo` carries capability flags; the HA provider sets them as before (no attendees). Extend the abstractions and the HA provider. Verify the tests and existing HA calendar tests pass.
- [x] 2.2 Write failing `CompositeCalendarProvider` tests: lists calendars of both providers; routes by id; an unknown id fails with a not-found error; one provider failing still returns the other's calendars with a warning. Implement and register it as the `ICalendarProvider`. Verify the tests pass.

## 3. CalDAV client and provider

- [x] 3.1 Record PROPFIND and REPORT responses from Nextcloud, Radicale and iCloud as fixtures. Write failing `HttpCalDavClient` tests: discovery (principal, home set, collections, user address), `calendar-query` with time range, `sync-collection` with and without token, invalid token handling, GET / PUT with `If-Match`, Basic auth from the secret store, 401 mapped to an auth error. Implement `ICalDavClient`. Verify the tests pass.
- [x] 3.2 Write failing `CalDavCalendarProvider` tests: events with organizer and attendees (RSVP mapping); recurrence expansion in range across DST; create; series update; occurrence update writes a `RECURRENCE-ID` override; ids `caldav:<account>/<calendar>`; discovery or auth failure raises a health warning without affecting others. Implement. Verify the tests pass.
- [x] 3.3 Write a Radicale Testcontainer integration test (run with `sg docker`): discover, create, list, occurrence update, sync. Verify it passes.

## 4. Tools

- [x] 4.1 Write failing `calendar_find_free_time` tests: busy merge across calendars of both providers, transparent and declined events are free, `within` clipping, minimum duration, at most 20 results, owner time zone, trusted output. Implement. Verify the tests pass.
- [x] 4.2 Write failing tests for `calendar_update_event` `scope` (occurrence and series on CalDAV; validation error for providers without occurrence edits; still confirm tier) and attendee fields in `calendar_list_events` (still untrusted). Implement. Verify the tests pass.

## 5. Change events

- [x] 5.1 Write failing `CalDavSyncPoller` tests (`FakeTimeProvider`, fake client): first poll stores state without events; created, updated and removed resources publish the matching events with id-only payloads; invitation detection by account address and `NEEDS-ACTION`; invalid sync token triggers a silent resync; ETag fallback; state survives restart. Add `caldav_sync_state` (migration, `erd.puml`) and implement. Verify the tests pass.
- [x] 5.2 Write a failing trigger matcher test that a watch on `source: calendar, type: invitation` fires on the published event. Verify it passes with the existing matcher (or fix the matcher if needed).

## 6. Demo and docs

- [x] 6.1 Extend the `Marshal.Demo` calendar simulation with attendees, a recurring event and a simulated `calendar.invitation` event; write failing demo tests first. Verify the demo tests pass without network access.
- [x] 6.2 Write the calendar guide (HA vs. CalDAV, setup for Nextcloud, Radicale, Fastmail, iCloud app passwords, change events with a watch example) and the configuration reference entries; add a commented `calendar.caldav` example to `deploy/config.example/marshal.yaml`. Verify the example config loads in the config load test and the pages follow `docs/development/docs-style.md`.

## 7. Integration check

- [x] 7.1 Run `dotnet test` (including the Testcontainer test with `sg docker`) and `openspec validate caldav --strict`. Verify both pass.
