# config-view Specification

## Purpose
TBD - created by archiving change extensibility. Update Purpose after archive.
## Requirements
### Requirement: Read-only config view
The web UI SHALL show the loaded skills, templates, MCP servers (with status and tool count), risk rules, models (without secrets) and satellite profiles, plus the last reload error per file. The view SHALL NOT edit files. Access SHALL require the `admin` scope.

#### Scenario: Failed reload visible
- **WHEN** the last reload of `risk.yaml` failed
- **THEN** the config view shows the error and that the previous rules are active

#### Scenario: Non-admin
- **WHEN** a user without `admin` opens the config API
- **THEN** the response is 403

