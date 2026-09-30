# event-bus Specification

## Purpose
TBD - created by archiving change background-work. Update Purpose after archive.
## Requirements
### Requirement: Normalized events
Event sources SHALL publish events normalized to `{ source, type, entity, payload, ownerId?, time }` on an in-process `Channel<T>`-based EventBus.

#### Scenario: HA state change
- **WHEN** HA reports `binary_sensor.kitchen_occupancy` changing to `on`
- **THEN** an event `{ source: ha, type: state_changed, entity: binary_sensor.kitchen_occupancy }` with old and new state is published

### Requirement: Event sources
The system SHALL provide sources for: HA state changes and `mobile_app_notification_action` events (HA websocket); Moonraker semantic print events derived from state transitions (`started`, `paused`, `resumed`, `complete`, `error`, `cancelled`, klippy shutdown, progress milestones, first layer done) — raw temperature diffs SHALL NOT be published; new mail (IMAP IDLE on INBOX, polling fallback); executor callbacks.

#### Scenario: Print complete
- **WHEN** `print_stats.state` changes from `printing` to `complete`
- **THEN** exactly one `printer.complete` event is published

#### Scenario: Temperature noise
- **WHEN** only extruder temperature diffs arrive
- **THEN** no event is published

#### Scenario: Missed transition after reconnect
- **WHEN** the Moonraker socket reconnects and the re-read state is `complete` while the cache had `printing`
- **THEN** a `printer.complete` event is published

### Requirement: Deterministic trigger matching
Event triggers and watches SHALL use declarative filters (source, type, entity glob, payload field conditions) matched deterministically, without a model call, before any task run starts.

#### Scenario: Watch fires once per match
- **WHEN** a watch filters `source: ha, entity: person.lukas, to: home` and that state change happens
- **THEN** the watch's task becomes due with the event as trigger payload

#### Scenario: Non-matching event
- **WHEN** an event does not match any filter
- **THEN** no model call is made

