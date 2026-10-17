## MODIFIED Requirements

### Requirement: Owner on every user-owned row
Every user-owned table, child tables included, SHALL have a non-null `owner_id` of type `OwnerId`. Exceptions: ASP.NET Identity tables, `UserPreferences` (keyed by user id), `Secret` (null = instance-scoped) and MCP tool pins (instance configuration without an owner column). A null owner SHALL mean instance-scoped only on `Secret`; shared household data later SHALL use an explicit scope column.

#### Scenario: Child row carries the owner
- **WHEN** a run step is written for a run owned by A
- **THEN** the run step row has `owner_id = A`

#### Scenario: Tool pins have no owner
- **WHEN** a tool pin is written
- **THEN** the row has no owner column and is readable by the MCP client regardless of the execution context
