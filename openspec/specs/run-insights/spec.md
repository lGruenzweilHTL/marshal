# run-insights Specification

## Purpose
Views that explain what Marshal did and what it cost: runs list and detail, cost, the dashboard shell and the system view.

## Requirements

### Requirement: Runs list and detail
The runs view SHALL list the owner's runs (voice, web, task, api) with filters by source, outcome and cost. The run detail SHALL show the context build, model calls (tokens, latency, time to first token, cost, reasoning collapsed by default or a withheld note), tool calls (args, truncated or redacted result, risk tier, guard decision, confirmation reasons, taint sources) and the reply.

#### Scenario: Guard decision visible
- **WHEN** a run contains a tool call escalated by taint
- **THEN** the detail shows guard decision `escalated_by_taint` and the taint source

#### Scenario: Redacted result
- **WHEN** a tool result was stored as a reference
- **THEN** the detail shows the reference, not content

#### Scenario: Reasoning in the run detail
- **WHEN** a model-call step holds reasoning
- **THEN** the detail shows it collapsed under that model call, and expands it on request

#### Scenario: Voice timeline
- **WHEN** a voice run spent 300 ms building context, 1.2 s in a model call and 2 s in a tool, and sent its first delta after 1.6 s
- **THEN** the detail shows these segments on one timeline with the first delta marked

#### Scenario: Slowest voice turns
- **WHEN** the user sorts the runs list by time to first delta, descending
- **THEN** the voice run with the longest time to first delta is listed first

### Requirement: Cost view
The cost view SHALL show cost per turn, per day and per tier, with the advisor on its own line. Displayed costs SHALL be the same numbers the budgets use.

#### Scenario: Advisor line
- **WHEN** a day includes advisor calls
- **THEN** the cost view shows advisor cost separately from the other tiers

### Requirement: Dashboard shell
Home SHALL be the landing page and SHALL be built from widgets, each reading existing API data, ordered by what needs the user. It SHALL include widgets for recent runs, today's cost and system health; a health warning SHALL move the health widget up.

#### Scenario: Health warning on dashboard
- **WHEN** `/health` reports a warning
- **THEN** Home shows the health widget with the warning

### Requirement: System view
The system view SHALL show health details, version information and the kill switch, and SHALL link to the configuration view for admins.

#### Scenario: Version shown
- **WHEN** the system view is opened
- **THEN** it shows the app version and the supported API versions

#### Scenario: Kill switch in the system view
- **WHEN** the system view is opened while no task runs
- **THEN** the kill switch is shown with its current state
