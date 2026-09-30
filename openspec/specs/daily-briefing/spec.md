# daily-briefing Specification

## Purpose
The morning briefing, built only from configuration (template, skill and task) on top of the task system, with no briefing-specific code.
## Requirements
### Requirement: Briefing is only a task
The daily briefing SHALL be implemented only through generic mechanisms: an agent template `briefing.md`, a skill with format guidelines (voice ~60–90 s, most important first; long version with links, RSS summaries, draft previews), the `rss-digest` skill, and a named task `briefing`. There SHALL be no briefing-specific code, endpoints or settings.

#### Scenario: No briefing code
- **WHEN** the source tree is searched for briefing-specific types or endpoints
- **THEN** none exist outside example configuration and tests

### Requirement: Example briefing task
The example configuration SHALL include a briefing task definition: cron trigger early morning, deliveries voice (`followUp: true`, `when: [state($person = home), state(<room occupancy> = on)]`, `expires` late morning → fallback push) and inbox, language from the owner's preference.

#### Scenario: Presence-based delivery
- **WHEN** the briefing is prepared at 06:00 and the owner enters the kitchen at 07:30
- **THEN** the `speech` is spoken there at 07:30 and the satellite listens for a follow-up

### Requirement: On-request briefing
A request like "good morning" SHALL be handled by the agent with `tasks.status("briefing")`, reading the prepared result if fresh, otherwise calling `tasks.run_now("briefing")`, which backgrounds the turn and delivers through the task's deliveries.

#### Scenario: Fresh result
- **WHEN** the briefing ran 30 minutes ago and the user asks for it
- **THEN** the prepared `speech` is read without a new run

