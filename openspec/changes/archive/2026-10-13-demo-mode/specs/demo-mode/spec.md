# Spec Delta

## Purpose

Lets an instance with only a model configured show Marshal's views and tool flows, using simulated integrations and seeded history that can be removed again.

## ADDED Requirements

### Requirement: Demo configuration
The system SHALL read `demo.enabled` (default `false`) and `demo.showBadge` (default `true`). Demo mode SHALL be off unless `demo.enabled` is `true`. Demo mode SHALL NOT change the requirement for a configured model provider.

#### Scenario: Default
- **WHEN** `marshal.yaml` has no `demo` section
- **THEN** no integration is simulated and no data is seeded

#### Scenario: No model
- **WHEN** demo mode is on and no model is configured
- **THEN** startup fails in the same way as without demo mode

### Requirement: Simulated integrations
With demo mode on, the system SHALL replace each of Home Assistant, printers, mail, RSS and notes with a simulated integration when that integration is not configured, and SHALL keep each configured integration real. A simulated integration SHALL offer the same tools, with the same ids, risk tiers and guard rules, as the real integration. It SHALL never contact a network host. The simulated mail integration SHALL have no send capability, the same as the real one. When Home Assistant is simulated and no MCP server with namespace `ha` is configured, the simulated Home Assistant integration SHALL also offer the home control tools of HA's MCP server that Marshal relies on (turning entities on and off, setting light brightness and climate temperature, reading the live context), with the same tool ids, backed by the simulated entity states.

#### Scenario: Only a model configured
- **WHEN** demo mode is on and none of the five integrations is configured
- **THEN** the model sees the Home Assistant, printer, mail, RSS and notes tools, and they return simulated data

#### Scenario: Real integration kept
- **WHEN** demo mode is on and FreshRSS is configured
- **THEN** the RSS tools use the configured FreshRSS server, and the other four integrations are simulated

#### Scenario: Confirm tier still applies
- **WHEN** the model asks to cancel the simulated print
- **THEN** the action is frozen as a pending action and the print continues until the user confirms

### Requirement: Simulated behavior
Simulated integrations SHALL behave consistently within one process run. Home Assistant service calls SHALL change the simulated entity states, and the change SHALL be visible in later state reads and events. The simulated printer SHALL advance its print progress over time and SHALL return snapshot images. A mail draft SHALL appear in later draft listings of the same process run. Simulated state MAY reset on restart.

#### Scenario: Light turned on
- **WHEN** the model turns on `light.kitchen` through the Home Assistant tools
- **THEN** a later state read of `light.kitchen` returns `on`

#### Scenario: Print progresses
- **WHEN** the printer state is read twice, ten minutes apart
- **THEN** the second read shows higher progress, until the print finishes

### Requirement: Seeded data
When demo mode is on and the instance owner exists, the system SHALL seed demo data for that owner once: conversations (web, and voice from at least two satellites, including a backgrounded turn with its result), runs with steps, tasks with results, notifications, one pending approval and memory chunks with edges. Seeded times SHALL be relative to the seed time. The system SHALL record every seeded row. Starting again with demo mode on SHALL NOT seed a second time.

#### Scenario: First start
- **WHEN** the instance starts with demo mode on and an owner, and nothing is seeded yet
- **THEN** the chat history, Inbox, tasks, runs and memory views show the seeded data

#### Scenario: Restart
- **WHEN** the instance restarts with demo mode still on
- **THEN** no rows are seeded again

#### Scenario: Owner created later
- **WHEN** demo mode is on and the first-run setup creates the owner
- **THEN** the data is seeded for that owner without a restart

### Requirement: Reset and reseed
`marshal demo reset` SHALL delete exactly the recorded seeded rows and their records, and SHALL keep every other row. `marshal demo seed` SHALL seed again when nothing is seeded. Both SHALL be written to the audit log with actor kind `system`.

#### Scenario: User data survives reset
- **WHEN** the user created a task during the demo and an operator runs `marshal demo reset`
- **THEN** the seeded rows are gone and the user's task still exists

#### Scenario: Reseed
- **WHEN** an operator runs `marshal demo reset` and then `marshal demo seed`
- **THEN** the seeded data exists again, with times relative to the new seed time

### Requirement: Demo marking in the UI
With demo mode on and `demo.showBadge` true, the navigation SHALL show a "Demo" badge in both layouts. The system view SHALL mark each simulated integration as simulated, whatever `demo.showBadge` is set to.

#### Scenario: Badge hidden for screenshots
- **WHEN** demo mode is on and `demo.showBadge` is false
- **THEN** no badge is shown in the navigation, and the system view still marks the simulated integrations
