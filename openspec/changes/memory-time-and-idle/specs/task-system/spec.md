## ADDED Requirements

### Requirement: Task preparation
An agent task with a `cron` trigger MAY declare `prepare: { before, goal }`, where `before` is a duration (at most 12 hours). The system SHALL run a preparation run of the task `before` ahead of each scheduled run, with the task's template, tool policy and budget, and the preparation `goal`. The preparation run's result `body` SHALL be stored as the task's prepared notes together with its taint sources. The next main run SHALL receive the prepared notes in its context and SHALL be tainted with their taint sources. Prepared notes SHALL be discarded after the main run or when they are older than `before` plus one hour. A failed preparation SHALL NOT stop the main run.

#### Scenario: Briefing prepared at night
- **WHEN** the briefing task runs at 06:00 with `prepare: { before: 3h, goal: "Collect today's appointments and the top news" }`
- **THEN** a preparation run happens at 03:00, and the 06:00 run sees its notes

#### Scenario: Taint carries over
- **WHEN** the preparation run read calendar events
- **THEN** the main run starts tainted with source `calendar`

#### Scenario: Preparation failed
- **WHEN** the preparation run fails
- **THEN** the main run runs at its time without prepared notes
