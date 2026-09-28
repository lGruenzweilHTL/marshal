## ADDED Requirements

### Requirement: Per-user preferences
Each user SHALL have preferences: language (`de` | `en`), time zone (IANA id), quiet hours (weekly schedule), notification targets (HA `person.*` entity and push targets) and DND end time. They SHALL be readable and writable through `/api/v1/me/preferences` and the preferences view.

#### Scenario: Update language
- **WHEN** the user sets language to `de`
- **THEN** runs without a request language use German

#### Scenario: Invalid time zone
- **WHEN** the user submits time zone `Mars/Base`
- **THEN** the update is rejected with a validation error

### Requirement: Time zone default
A new user's time zone SHALL default to the configured instance time zone (Home Assistant's time zone once the HA client exists, otherwise `instance.timeZone` from config).

#### Scenario: Default on creation
- **WHEN** the first user is created with instance time zone `Europe/Vienna`
- **THEN** the user's time zone is `Europe/Vienna`

### Requirement: Softer preferences live in memory
Preferences about content (briefing topics, interests, length) SHALL NOT be stored in user preferences; they belong to memory `preference` chunks.

#### Scenario: No briefing settings
- **WHEN** the preferences API schema is inspected
- **THEN** it contains no briefing or interest fields
