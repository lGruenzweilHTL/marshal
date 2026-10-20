## MODIFIED Requirements

### Requirement: Per-user preferences
Each user SHALL have preferences: language (`de` | `en`), time zone (IANA id), quiet hours (weekly schedule), notification targets (HA `person.*` entity and push targets), DND end time and "help Marshal learn" (boolean, default off). They SHALL be readable and writable through `/api/v1/me/preferences` and the preferences view. "Help Marshal learn" SHALL gate every feature that learns from the user's behavior or proposes things on its own; the preferences view SHALL list which features it enables.

#### Scenario: Update language
- **WHEN** the user sets language to `de`
- **THEN** runs without a request language use German

#### Scenario: Invalid time zone
- **WHEN** the user submits time zone `Mars/Base`
- **THEN** the update is rejected with a validation error

#### Scenario: Learning off by default
- **WHEN** a new user is created
- **THEN** "help Marshal learn" is off
