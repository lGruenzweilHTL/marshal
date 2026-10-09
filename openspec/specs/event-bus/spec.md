# event-bus Specification

## Purpose
The event bus for background work: normalized events from Home Assistant, printers, mail and executors, and deterministic trigger matching.

## Requirements

### Requirement: Normalized events
Event sources SHALL publish events normalized to `{ source, type, entity, payload, ownerId?, time }` on an in-process `Channel<T>`-based EventBus.

#### Scenario: HA state change
- **WHEN** HA reports `binary_sensor.kitchen_occupancy` changing to `on`
- **THEN** an event `{ source: ha, type: state_changed, entity: binary_sensor.kitchen_occupancy }` with old and new state is published

### Requirement: Event sources
The system SHALL provide sources for: HA state changes and `mobile_app_notification_action` events (HA websocket); Moonraker semantic print events derived from state transitions (`started`, `paused`, `resumed`, `complete`, `error`, `cancelled`, klippy shutdown, progress milestones, first layer done) — raw temperature diffs SHALL NOT be published; new mail (IMAP IDLE on INBOX, polling fallback); executor callbacks; webhook sources received through the webhook ingest (e.g. `uptime-kuma`), each shown with the time of its last received webhook.

#### Scenario: Print complete
- **WHEN** `print_stats.state` changes from `printing` to `complete`
- **THEN** exactly one `printer.complete` event is published

#### Scenario: Temperature noise
- **WHEN** only extruder temperature diffs arrive
- **THEN** no event is published

#### Scenario: Missed transition after reconnect
- **WHEN** the Moonraker socket reconnects and the re-read state is `complete` while the cache had `printing`
- **THEN** a `printer.complete` event is published

#### Scenario: Webhook source listed
- **WHEN** an Uptime Kuma webhook was received at 10:15
- **THEN** the event sources list on the system page shows `uptime-kuma` with last received 10:15

### Requirement: Deterministic trigger matching
Event triggers and watches SHALL use declarative filters (source, type, entity glob, payload field conditions) matched deterministically, without a model call, before any task run starts. A watch SHALL fire at most once per arming; a once-watch is armed only once.

#### Scenario: Watch fires once per match
- **WHEN** a watch filters `source: ha, entity: person.lukas, to: home` and that state change happens
- **THEN** the watch's task becomes due with the event as trigger payload

#### Scenario: Non-matching event
- **WHEN** an event does not match any filter
- **THEN** no model call is made

#### Scenario: Once-watch fires a single time
- **WHEN** a once-watch filters `source: ha, entity: person.lukas, to: Hornbach`, the owner enters the zone, the run finishes, and the owner enters the zone again the next day
- **THEN** the task ran exactly once

#### Scenario: Leaving a zone
- **WHEN** a watch filters `source: ha, entity: person.lukas, from: Work` and the person's state changes from `Work` to `not_home`
- **THEN** the watch's task becomes due

### Requirement: Event payload trust
Event payloads SHALL be untrusted by default: a run started by an event SHALL get the payload wrapped as untrusted data and SHALL be tainted with source `event`. An event SHALL be trusted only when its source declares it trusted in code; a run started by a trusted event SHALL get the payload unwrapped and SHALL NOT be tainted by it. Trust SHALL NOT be settable from configuration or from the event's content.

#### Scenario: HA event stays untrusted
- **WHEN** a watch on `person.lukas` fires
- **THEN** its run receives the payload wrapped as untrusted data and is tainted with source `event`

#### Scenario: Trusted webhook event
- **WHEN** a watch on `source: uptime-kuma, type: monitor.down` fires
- **THEN** its run receives the payload unwrapped and is not tainted

#### Scenario: Spoofed trust in payload
- **WHEN** an untrusted event's payload contains a field `trusted: true`
- **THEN** its run is still tainted with source `event`
