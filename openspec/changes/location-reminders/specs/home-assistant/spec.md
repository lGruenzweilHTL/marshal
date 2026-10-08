## ADDED Requirements

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
