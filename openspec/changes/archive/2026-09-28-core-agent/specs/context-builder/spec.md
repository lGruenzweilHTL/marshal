## ADDED Requirements

### Requirement: Context assembly
The system SHALL build the model context from ordered contributors: base system prompt, user profile, time and locale, satellite context, output-mode rules, and contributions registered by later capabilities (memory, skills, templates). Each contributor SHALL implement `IContextContributor`. No user name or personal data SHALL be hardcoded in prompts.

#### Scenario: Contributors in order
- **WHEN** two contributors are registered with different order values
- **THEN** their text appears in the system prompt in that order

#### Scenario: User profile from context
- **WHEN** the owner's display name is "Lukas"
- **THEN** the system prompt names the user from the owner's profile, not from a constant

### Requirement: Output mode
The system SHALL derive the output mode from the turn's `inputMode`: voice → plain speakable text, no markdown, short, numbers written as spoken; text → markdown allowed.

#### Scenario: Voice turn
- **WHEN** `inputMode = voice`
- **THEN** the system prompt contains the voice output rules and no markdown rules

### Requirement: Reply language
The system SHALL reply in the request language (`de` or `en`). Runs without a request language SHALL use the owner's stored language preference. System prompts SHALL stay English; dates and numbers SHALL be formatted in the reply language, in the owner's time zone.

#### Scenario: German request
- **WHEN** a turn arrives with `language = de`
- **THEN** the system prompt instructs the model to reply in German and shows the local time in German format

### Requirement: Untrusted content is wrapped
Results of tools with `untrustedOutput` SHALL be wrapped as data in the context (clearly delimited, labeled as untrusted, never as instructions).

#### Scenario: Mail body in context
- **WHEN** a tool with `untrustedOutput` returns text containing "ignore previous instructions"
- **THEN** the text appears inside the untrusted-data wrapper in the model input
