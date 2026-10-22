# maintenance Specification

## Purpose
Tracks maintenance that depends on usage (print hours, cycles, consumption) and reminds the user when an item is due.

## Requirements

### Requirement: Maintenance configuration
Maintenance items SHALL be configured in `marshal.yaml` as `maintenance.items`, a map from item id (`[a-z][a-z0-9_]*`) to `{ name, source, every, warnBefore? }`. `source` SHALL be either `{ ha: <entity id or statistic id> }` (a numeric, increasing counter) or `{ printer: <printer name>, metric: printHours | filamentMeters }`. `every` and `warnBefore` SHALL be positive numbers in the source's unit, with `warnBefore` less than `every`. Invalid items SHALL fail startup with a configuration error naming the key. Without the section, no `maintenance` tool SHALL exist.

#### Scenario: Printer nozzle
- **WHEN** `maintenance.items.voron_nozzle` is `{ name: "Voron nozzle", source: { printer: voron, metric: printHours }, every: 300, warnBefore: 20 }`
- **THEN** the item is tracked in print hours of printer `voron`

#### Scenario: Unknown printer
- **WHEN** an item names a printer that is not configured
- **THEN** startup fails naming `maintenance.items.<id>.source.printer`

### Requirement: Counters and due items
For each item the system SHALL store a baseline (the counter value when it was last done) and the time it was last done; on first sight the baseline SHALL be the current value. Usage SHALL be the current value minus the baseline; when the counter value drops below the baseline (a reset), the baseline SHALL become the new value and usage SHALL continue from there. When usage reaches `every - warnBefore`, the owner SHALL get one `normal` notification that the item is due soon; when usage reaches `every`, one `normal` notification that it is due. Counters SHALL be read on relevant events (HA state changes of the entity, printer job completion) and at least hourly.

#### Scenario: Due
- **WHEN** the nozzle's baseline is 1,200 print hours and Moonraker reports 1,500
- **THEN** the owner is notified once that the Voron nozzle is due

#### Scenario: Counter reset
- **WHEN** a sensor's value drops from 80 to 2
- **THEN** the baseline becomes 2 and no notification is sent

### Requirement: Maintenance tools
The system SHALL provide `maintenance.status(item?)` (safe), returning per item its name, usage, interval, unit, state (`ok`, `soon`, `due`) and when it was last done, and `maintenance.done(item)` (safe, `persists`), which sets the baseline to the current counter value and the last-done time to now. Both SHALL be hidden without configured items.

#### Scenario: Mark done
- **WHEN** the user says "I changed the Voron nozzle" and the model calls `maintenance_done` with `voron_nozzle`
- **THEN** the item's usage is 0 and its state is `ok`

#### Scenario: Done in a tainted run
- **WHEN** a run that read mail calls `maintenance_done`
- **THEN** a pending action with reason `taint` is created

### Requirement: Maintenance widget
Home SHALL show a maintenance widget listing items in state `soon` or `due` first, with usage, interval and a "done" action that requires a confirmation dialog. The widget SHALL be hidden without configured items.

#### Scenario: Due item on Home
- **WHEN** one item is due
- **THEN** Home shows the widget with that item first

### Requirement: Suggested maintenance items
With the owner's "help Marshal learn" preference on, a suggestion detector `maintenance_counter` SHALL propose items for counters that have no configured item (configured printers' print hours; HA sensors with state class `total_increasing` whose name suggests runtime, cycles or hours). Its suggestion SHALL include a YAML snippet for `maintenance.items` and SHALL follow the suggestion rules (estimate, delivery, muting).

#### Scenario: Printer without item
- **WHEN** printer `voron` is configured, no item uses it, and the toggle is on
- **THEN** a suggestion proposes a nozzle item with a YAML snippet
