## MODIFIED Requirements

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
