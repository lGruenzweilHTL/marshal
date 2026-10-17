# config-view Specification

## Purpose
A read-only web view of the effective configuration, so the user can see what Marshal runs with without seeing secrets.
## Requirements
### Requirement: Read-only config view
The web UI SHALL show the loaded skills, templates, MCP servers (with status and tool count), risk rules, models (without secrets) and satellite profiles, plus the last reload error per file. The view SHALL NOT edit files; its only write action SHALL be deciding MCP tool approvals. The view SHALL be listed under Settings and reachable from the system view, and SHALL be shown only to users with the `admin` role. Access to the config API SHALL require the `admin` scope.

#### Scenario: Failed reload visible
- **WHEN** the last reload of `risk.yaml` failed
- **THEN** the config view shows the error and that the previous rules are active

#### Scenario: Non-admin
- **WHEN** a user without `admin` opens the config API
- **THEN** the response is 403

#### Scenario: Hidden for non-admins
- **WHEN** a user without the `admin` role opens the system view
- **THEN** no link to the configuration view is shown

### Requirement: MCP tool approvals
For each MCP server, the config view SHALL list tools waiting for approval with the change kind (`new` | `changed`) and, for changed tools, the previous and current description and schemas side by side with the differences marked. An admin SHALL approve or reject each tool. Approving SHALL make the current version trusted; rejecting SHALL keep the tool hidden until its fingerprint changes again. Both SHALL be audited with the user as actor.

#### Scenario: Approve a changed description
- **WHEN** an admin approves `paperless.search` after reviewing the changed description
- **THEN** the tool is visible from the next turn and the audit log has an entry with the admin as actor

#### Scenario: Reject
- **WHEN** an admin rejects a new tool
- **THEN** the tool stays hidden, and it is offered for approval again only when its fingerprint changes
