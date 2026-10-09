# home-assistant Specification

## Purpose
The Home Assistant integration: connection, registry and state cache, target resolution, area context, timers and the instance time zone.

## Requirements

### Requirement: HA connection
The system SHALL connect to Home Assistant with `ha.url` and a long-lived token referenced by `ha.tokenSecret` (required secret). It SHALL use HA's REST and websocket APIs for system calls and HA's MCP server (configured as MCP server id `ha`) for model-driven home control.

#### Scenario: Token missing
- **WHEN** `ha.url` is configured and `ha.tokenSecret` does not resolve
- **THEN** startup fails naming the reference

### Requirement: Registry and state cache
The system SHALL cache HA's entity, device, area and floor registries and the set of entities exposed to Assist, and SHALL keep entity states fresh through a websocket subscription. After a reconnect it SHALL reload registries and states.

#### Scenario: Entity renamed
- **WHEN** an entity is renamed in HA
- **THEN** the cache reflects the new name after the registry update event

#### Scenario: Reconnect
- **WHEN** the websocket reconnects after an outage
- **THEN** registries and states are reloaded before the resolver answers again

### Requirement: HA target resolver
The system SHALL register an `IRiskTargetResolver` for the `ha` namespace that expands a call's `name`, `area`, `floor`, `domain` and `device_class` arguments to the concrete exposed entities, each with domain and device class. Unknown names or empty results SHALL be reported as unresolved.

#### Scenario: Area expansion
- **WHEN** `ha.intent__HassTurnOff` is called with `area = hall` and the hall has two lights and a lock
- **THEN** the resolver returns all three entities, and a lock rule makes the call `confirm`

#### Scenario: Non-exposed entity
- **WHEN** a call names an entity that is not exposed to Assist
- **THEN** the resolver reports it as unresolved

### Requirement: Area context without dumps
The context builder SHALL include states of exposed entities in the turn's area and of entities mentioned in the user's text, taken from the cache. It SHALL NOT include all entity states; the model SHALL call `ha.homeassistant__GetLiveContext` for more.

#### Scenario: Kitchen turn
- **WHEN** a turn comes from the kitchen
- **THEN** the system prompt lists kitchen entity states and no entities of other areas that were not mentioned

### Requirement: Timers
If the HA MCP server offers timer intents usable without device context, timers SHALL use them. Otherwise the system SHALL provide `timers.start(duration, name?)`, `timers.cancel(name?)` and `timers.status()` (safe), which call HA's intent handling with the turn's device id, and SHALL hide them in turns without a device.

#### Scenario: Timer from a satellite
- **WHEN** a voice turn from a satellite asks for a 10-minute timer and the fallback tools are active
- **THEN** HA starts a timer bound to that satellite's device

#### Scenario: No device
- **WHEN** a web chat turn has no device id
- **THEN** no `timers_*` tool is visible

### Requirement: Instance time zone from HA
When `instance.timeZone` is not configured, the instance time zone SHALL be HA's configured time zone.

#### Scenario: HA time zone
- **WHEN** HA reports `Europe/Vienna` and `instance.timeZone` is unset
- **THEN** new users default to `Europe/Vienna`

### Requirement: Places
The system SHALL provide `places.find(query?)` (safe) and `places.create_here(name, radius?)` (confirm) as built-in tools over Home Assistant zones. Both SHALL be hidden while no Home Assistant connection is configured; `places.create_here` SHALL also be hidden when the owner has no person entity configured. The tools SHALL only ever read the owner's own configured person entity, never another person's.

`places.find` SHALL return the owner's person entity id and list zones (`zone.*` entities) from the state cache, filtered by `query` when given (case-insensitive match on zone name or id), each with its zone id, name, radius in meters, whether the owner is inside it now, and the state value the owner's person entity takes in that zone (`home` for `zone.home`, otherwise the zone's name). Passive zones SHALL be listed and marked passive, because a person entity never takes their name as state. While the cache is not loaded, the call SHALL fail with an error saying Home Assistant is unavailable.

`places.create_here` SHALL create a zone through the Home Assistant websocket command `zone/create` with the given name, the latitude and longitude of the owner's person entity, and `radius` in meters (default 150, allowed 50–1000). It SHALL fail without a call to Home Assistant when the person entity has no GPS position or when a zone with the same name (case-insensitive) already exists. It SHALL return the new zone with the same fields as `places.find`.

#### Scenario: Find a zone by spoken name
- **WHEN** the cache holds `zone.hornbach_linz` named `Hornbach Linz` and the model calls `places_find` with `hornbach`
- **THEN** the result lists `zone.hornbach_linz` with name `Hornbach Linz` and person state value `Hornbach Linz`

#### Scenario: Home zone
- **WHEN** the model calls `places_find` with `home`
- **THEN** the result lists `zone.home` with person state value `home`

#### Scenario: Owner inside a zone
- **WHEN** the owner's person entity has state `Hornbach Linz` and the model calls `places_find` without a query
- **THEN** `zone.hornbach_linz` is marked as the zone the owner is in

#### Scenario: Create a zone at the current position
- **WHEN** the owner's person entity has latitude 48.30 and longitude 14.29 and the user confirms `places_create_here` with name `Baumarkt`
- **THEN** Home Assistant receives `zone/create` with name `Baumarkt`, latitude 48.30, longitude 14.29 and radius 150

#### Scenario: Creating a zone needs confirmation
- **WHEN** the model calls `places_create_here`
- **THEN** a pending confirmation is created and no `zone/create` command is sent until the user confirms

#### Scenario: No GPS position
- **WHEN** the owner's person entity has no latitude attribute and the user confirms `places_create_here`
- **THEN** the call fails with an error saying the current position is unknown, and no command is sent

#### Scenario: Duplicate zone name
- **WHEN** a zone named `Baumarkt` exists and the user confirms `places_create_here` with name `baumarkt`
- **THEN** the call fails naming the existing zone, and no command is sent

#### Scenario: Home Assistant rejects the zone
- **WHEN** Home Assistant answers `zone/create` with an error (e.g. the token's user is not an administrator)
- **THEN** the call fails with Home Assistant's error message

#### Scenario: No person entity configured
- **WHEN** the owner has no person entity in their notification targets
- **THEN** `places_create_here` is not visible and `places_find` reports no owner presence

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
