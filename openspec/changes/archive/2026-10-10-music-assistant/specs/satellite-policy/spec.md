# Spec Delta

## MODIFIED Requirements

### Requirement: Satellite profiles
The system SHALL read optional satellite profiles from `/config/satellites.yaml`, keyed by satellite entity id, with `verbosity`, `notes`, `denyTools`, `denyConfirm`, `display` (the name of a configured display surface in the satellite's room) and `speaker` (the name of a configured music speaker in the satellite's room). Unconfigured satellites and turns without `satelliteId` SHALL use defaults and have no display and no speaker. A `display` that names no configured surface, or a `speaker` that names no configured speaker, SHALL be a configuration error for that profile, named with the file and key. The file SHALL be hot-reloaded.

#### Scenario: Profile notes in the prompt
- **WHEN** a turn comes from `assist_satellite.kitchen` whose profile has `notes`
- **THEN** the notes appear in the satellite context lines of the system prompt

#### Scenario: Unknown satellite
- **WHEN** a turn comes from a satellite without a profile
- **THEN** default verbosity applies, no tools are denied and the turn has no display and no speaker

#### Scenario: Unknown display name
- **WHEN** `satellites.yaml` sets `display: kitchen_tv` and no display `kitchen_tv` is configured
- **THEN** the error names `satellites.yaml` and the profile's `display` key

#### Scenario: Unknown speaker name
- **WHEN** `satellites.yaml` sets `speaker: kitchen` and no speaker `kitchen` is configured
- **THEN** the error names `satellites.yaml` and the profile's `speaker` key

### Requirement: Satellite context
For each turn the system SHALL provide area and floor names (from `areaId`), local time, day of week and request language to the context builder through `ISatelliteContextProvider`. When music speakers are configured, it SHALL also provide the turn's speaker name (if the profile has one) and the configured speaker names.

#### Scenario: Area in context
- **WHEN** a turn carries `areaId = kitchen`
- **THEN** the system prompt says the user is speaking in the kitchen

#### Scenario: Speaker in context
- **WHEN** a turn comes from a satellite with `speaker: kitchen` and speakers `kitchen` and `living_room` are configured
- **THEN** the system prompt names `kitchen` as this room's speaker and lists both speaker names
