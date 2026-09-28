## ADDED Requirements

### Requirement: Task results
A task run SHALL produce a result with `speech` (short, speakable) and `body` (markdown). Voice channels SHALL use `speech`; push SHALL use `speech` plus a deep link to the result; the web UI SHALL show `body`.

#### Scenario: Push deep link
- **WHEN** a result is delivered by push
- **THEN** the push text is the `speech` and tapping it opens the task result view

### Requirement: Multiple deliveries
A task SHALL have a list of deliveries, each with channel (`voice` with target satellite / area / "where I am", `push`, `inbox`, `silent`), priority (`normal` default, or `low`), optional `followUp` (voice only), optional `when` conditions and optional `expires` with fallback channel. Each delivery SHALL be delivered independently through the NotificationRouter.

#### Scenario: Voice plus inbox
- **WHEN** a task has a voice delivery and an inbox delivery
- **THEN** both are delivered independently

### Requirement: Delivery validation
A delivery with `channel: voice` and `priority: low` SHALL be rejected at task creation or update with a validation error. `followUp` on a non-voice channel SHALL be rejected. Deliveries SHALL never have priority `critical`.

#### Scenario: Low-priority voice
- **WHEN** the model creates a task with a `low` voice delivery
- **THEN** `tasks_create` returns a validation error and no task is created

### Requirement: Conditional delivery
A delivery's `when` SHALL be a list of state conditions `state(<entity_id> <op> <value>)` with `=`, `!=`, `>`, `<` (numeric), all of which must hold; `$person` SHALL resolve to the owner's person entity. Conditions SHALL be checked when the result is ready and on every matching state-change event. If `expires` (owner's time zone) passes first, the delivery SHALL go out via the fallback channel.

#### Scenario: Deliver when someone enters the kitchen
- **WHEN** the result is ready at 06:05, `state(binary_sensor.kitchen_occupancy = on)` becomes true at 07:10
- **THEN** the delivery goes out at 07:10

#### Scenario: Expiry fallback
- **WHEN** the conditions never hold before `expires: 11:00` with fallback `push`
- **THEN** at 11:00 the result is delivered by push

#### Scenario: Areas are not entities
- **WHEN** a condition references `area.kitchen`
- **THEN** the task is rejected with a validation error
