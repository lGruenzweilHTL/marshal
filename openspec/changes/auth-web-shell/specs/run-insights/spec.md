## ADDED Requirements

### Requirement: Runs list and detail
The runs view SHALL list the owner's runs (voice, web, task, api) with filters by source, outcome and cost. The run detail SHALL show the context build, model calls (tokens, latency, time to first token, cost), tool calls (args, truncated or redacted result, risk tier, guard decision, confirmation reasons, taint sources) and the reply.

#### Scenario: Guard decision visible
- **WHEN** a run contains a tool call escalated by taint
- **THEN** the detail shows guard decision `escalated_by_taint` and the taint source

#### Scenario: Redacted result
- **WHEN** a tool result was stored as a reference
- **THEN** the detail shows the reference, not content

### Requirement: Cost view
The cost view SHALL show cost per turn, per day and per tier, with the advisor on its own line. Displayed costs SHALL be the same numbers the budgets use.

#### Scenario: Advisor line
- **WHEN** a day includes advisor calls
- **THEN** the cost view shows advisor cost separately from the other tiers

### Requirement: Dashboard shell
The dashboard SHALL be the landing page and SHALL be built from widgets, each reading existing API data. This change SHALL provide widgets for recent runs, today's cost and system health; later changes add their own widgets.

#### Scenario: Health warning on dashboard
- **WHEN** `/health` reports a warning
- **THEN** the dashboard health widget shows it

### Requirement: System view
The system view SHALL show health details and version information.

#### Scenario: Version shown
- **WHEN** the system view is opened
- **THEN** it shows the app version and the supported API versions
