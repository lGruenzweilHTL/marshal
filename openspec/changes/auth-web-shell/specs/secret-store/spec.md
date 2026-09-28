## ADDED Requirements

### Requirement: Secret management API and view
Users SHALL list, create, replace and delete DB secrets through `/api/v1/me/secrets` (own secrets) and `/api/v1/secrets/instance` (instance secrets, `admin` only). Values SHALL be write-only: no endpoint SHALL return a value. The list SHALL show name, scope (instance / user) and last update. Every change SHALL be audited without the value.

#### Scenario: Write-only
- **WHEN** a secret is created and then fetched through any endpoint
- **THEN** the response contains no value

#### Scenario: Non-admin instance secret
- **WHEN** a user without `admin` creates an instance secret
- **THEN** the response is 403

### Requirement: Environment secrets and unresolved references
The secrets view SHALL list secrets provided by environment or `/run/secrets` read-only with their source and no value, and SHALL flag every secret reference from `/config` or user-owned rows that does not resolve.

#### Scenario: Unresolved reference
- **WHEN** `/config/marshal.yaml` references `freshrss_api` and no source has it
- **THEN** the secrets view flags `freshrss_api` as unresolved and names where it is referenced
