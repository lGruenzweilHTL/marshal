# satellite-policy Specification

## Purpose
Per-satellite behavior: profiles, the satellite's context and which tools it may use.

## Requirements

### Requirement: Satellite profiles
The system SHALL read optional satellite profiles from `/config/satellites.yaml`, keyed by satellite entity id, with `verbosity`, `notes`, `denyTools`, `denyConfirm` and `display` (the name of a configured display surface in the satellite's room). Unconfigured satellites and turns without `satelliteId` SHALL use defaults and have no display. A `display` that names no configured surface SHALL be a configuration error for that profile, named with the file and key. The file SHALL be hot-reloaded.

#### Scenario: Profile notes in the prompt
- **WHEN** a turn comes from `assist_satellite.kitchen` whose profile has `notes`
- **THEN** the notes appear in the satellite context lines of the system prompt

#### Scenario: Unknown satellite
- **WHEN** a turn comes from a satellite without a profile
- **THEN** default verbosity applies, no tools are denied and the turn has no display

#### Scenario: Unknown display name
- **WHEN** `satellites.yaml` sets `display: kitchen_tv` and no display `kitchen_tv` is configured
- **THEN** the error names `satellites.yaml` and the profile's `display` key

### Requirement: Satellite context
For each turn the system SHALL provide area and floor names (from `areaId`), local time, day of week and request language to the context builder through `ISatelliteContextProvider`.

#### Scenario: Area in context
- **WHEN** a turn carries `areaId = kitchen`
- **THEN** the system prompt says the user is speaking in the kitchen

### Requirement: Satellite tool policy
Tools matching `denyTools` SHALL be hidden and denied. Confirm-tier tools matching `denyConfirm` SHALL be hidden; arg-aware tools matching `denyConfirm` SHALL stay visible and the guard SHALL deny calls that resolve to confirm with the error "not allowed from this device".

#### Scenario: Denied tool
- **WHEN** the kids-room profile has `denyTools: [mail.*]`
- **THEN** no `mail_*` tool is in the tool list for turns from that satellite

#### Scenario: Denied confirmation on arg-aware tool
- **WHEN** a satellite has `denyConfirm: [ha.*]` and a call resolves to a lock
- **THEN** the call is denied, no pending action is created, and the model receives the "not allowed from this device" error
