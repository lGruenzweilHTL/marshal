## MODIFIED Requirements

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
