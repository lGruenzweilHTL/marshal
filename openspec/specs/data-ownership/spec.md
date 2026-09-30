# data-ownership Specification

## Purpose
How user-owned data is kept apart: an owner on every row, owner-scoped queries, typed ids, migrations and expected failures as results.
## Requirements
### Requirement: Owner on every user-owned row
Every user-owned table, child tables included, SHALL have a non-null `owner_id` of type `OwnerId`. Exceptions: ASP.NET Identity tables, `UserPreferences` (keyed by user id) and `Secret` (null = instance-scoped). A null owner SHALL mean instance-scoped only on `Secret`; shared household data later SHALL use an explicit scope column.

#### Scenario: Child row carries the owner
- **WHEN** a run step is written for a run owned by A
- **THEN** the run step row has `owner_id = A`

### Requirement: Owner-scoped queries
All data access SHALL go through EF Core global query filters on the current execution context's owner. `Secret` SHALL also return instance rows (`owner_id is null`).

#### Scenario: No leakage between users
- **WHEN** the database contains sessions of owners A and B and code runs as A
- **THEN** every query returns only A's sessions

#### Scenario: Missing execution context
- **WHEN** code queries owner-scoped data without an execution context
- **THEN** the query fails instead of returning unfiltered data

### Requirement: Strongly typed ids
Domain entities SHALL use strongly typed ids (`OwnerId`, `SessionId`, `RunId`, `PendingActionId`, `ApiTokenId`, ...) stored through value converters. A user's id SHALL always be `OwnerId`.

#### Scenario: Wrong id type
- **WHEN** code passes a `RunId` where a `SessionId` is expected
- **THEN** it does not compile

### Requirement: Migrations
EF Core migrations SHALL be applied automatically at startup. Before applying any pending migration, the SQLite file SHALL be copied next to itself with a timestamp.

#### Scenario: Pending migration
- **WHEN** the app starts with one pending migration
- **THEN** a timestamped copy of the database exists before the migration runs

### Requirement: Expected failures as Result
Expected failures SHALL be returned as `Result` / `Result<T>` from `Marshal.Abstractions`, with a stable code and a message. API endpoints SHALL map failed results to `ProblemDetails`.

#### Scenario: Not found
- **WHEN** an endpoint's service returns a failed result with code `session.not_found`
- **THEN** the response is 404 ProblemDetails carrying that code

