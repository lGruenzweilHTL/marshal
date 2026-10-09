## MODIFIED Requirements

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

## ADDED Requirements

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
