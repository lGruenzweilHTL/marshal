## ADDED Requirements

### Requirement: Energy statistics
The system SHALL provide `energy.usage(period, from?, to?, compareWith?)` (safe) as a built-in tool over Home Assistant's energy dashboard configuration and long-term statistics. It SHALL be hidden while no Home Assistant connection is configured or while the energy dashboard has no energy sources.

`period` SHALL be one of `today`, `yesterday`, `week` (Monday 00:00 until now), `month` (the 1st 00:00 until now), `last_week`, `last_month` or `range` (`from` and `to` as local dates, `to` inclusive). Period boundaries SHALL be computed in the owner's time zone.

The result SHALL contain, for each configured source, the energy for the period in kWh (gas and water in their HA unit, e.g. m³): grid import, grid export, solar production, battery charge and discharge, gas and water; the household consumption (grid import + solar production + battery discharge − grid export − battery charge); cost and compensation in HA's currency when HA records cost statistics; and up to five individual devices with the highest consumption when the dashboard lists devices (devices nested under another device are not counted twice). Sources the dashboard does not configure SHALL be absent, not zero.

`compareWith: previous` SHALL add the same figures for the previous period of equal elapsed length: `today` → yesterday up to the same time, `week` → last week up to the same weekday and hour, `month` → last month up to the same day and hour (clamped to the month's last day), `last_month` → the month before it, `yesterday` / `last_week` / `range` → the period of equal length just before. Open periods (`today`, `week`, `month`) end at the current full hour, because long-term statistics are hourly.

The tool SHALL fail with an error saying Home Assistant is unavailable while the websocket is disconnected, and with a validation error for an unknown period, a `range` without both dates, or `from` after `to`.

#### Scenario: Usage this week
- **WHEN** the dashboard configures grid import `sensor.grid_import` and the model calls `energy_usage` with `week` on Thursday 2026-10-08 14:00 Vienna time
- **THEN** HA's statistics are read from Monday 2026-10-05 00:00 to Thursday 14:00 Vienna time and the result contains the grid import in kWh

#### Scenario: Compare with last week
- **WHEN** the model calls `energy_usage` with `week` and `compareWith: previous` on Thursday 14:00
- **THEN** the result also contains last week's figures from Monday 00:00 to Thursday 14:00

#### Scenario: Consumption with solar
- **WHEN** a day has grid import 8 kWh, solar production 12 kWh and grid export 5 kWh
- **THEN** the result's household consumption is 15 kWh

#### Scenario: Cost when available
- **WHEN** the grid import has a cost statistic and the model asks for `yesterday`
- **THEN** the result contains yesterday's cost in HA's configured currency

#### Scenario: No cost configured
- **WHEN** the grid import has no price and no cost statistic
- **THEN** the result contains no cost field

#### Scenario: Top devices
- **WHEN** the dashboard lists seven devices and one of them is included in another device's statistic
- **THEN** the result lists at most five top-level devices, ordered by consumption

#### Scenario: No energy dashboard
- **WHEN** HA is connected and the energy dashboard has no sources
- **THEN** no `energy_usage` tool is visible

#### Scenario: Invalid range
- **WHEN** the model calls `energy_usage` with `range`, `from: 2026-10-08` and `to: 2026-10-01`
- **THEN** the call fails with a validation error and no statistics are requested
