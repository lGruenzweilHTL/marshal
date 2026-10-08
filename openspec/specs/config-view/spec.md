# config-view Specification

## Purpose
A read-only web view of the effective configuration, so the user can see what Marshal runs with without seeing secrets.
## Requirements
### Requirement: Read-only config view
The web UI SHALL show the loaded skills, templates, MCP servers (with status and tool count), risk rules, models (without secrets) and satellite profiles, plus the last reload error per file. The view SHALL NOT edit files. The view SHALL be listed under Settings and reachable from the system view, and SHALL be shown only to users with the `admin` role. Access to the config API SHALL require the `admin` scope.

#### Scenario: Failed reload visible
- **WHEN** the last reload of `risk.yaml` failed
- **THEN** the config view shows the error and that the previous rules are active

#### Scenario: Non-admin
- **WHEN** a user without `admin` opens the config API
- **THEN** the response is 403

#### Scenario: Hidden for non-admins
- **WHEN** a user without the `admin` role opens the system view
- **THEN** no link to the configuration view is shown
