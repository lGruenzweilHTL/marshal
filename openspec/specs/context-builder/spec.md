# context-builder Specification

## Purpose
How the prompt context of a run is assembled: output mode, reply language, and untrusted content wrapped as data.
## Requirements
### Requirement: Context assembly
The system SHALL build the model context from ordered contributors: base system prompt, user profile, time and locale, satellite context, output-mode rules, and contributions registered by later capabilities (memory, skills, templates). Each contributor SHALL implement `IContextContributor` and SHALL declare a stability: `static` (same for every run with the same configuration), `session` (same for every turn of a session) or `turn` (may change every turn). The system prompt SHALL contain all `static` contributions first, then `session`, then `turn`, and within each stability the contributors' order values. No user name or personal data SHALL be hardcoded in prompts.

#### Scenario: Contributors in order
- **WHEN** two contributors with the same stability are registered with different order values
- **THEN** their text appears in the system prompt in that order

#### Scenario: Stable text first
- **WHEN** the time contributor (`turn`) has a lower order value than the skill index contributor (`static`)
- **THEN** the skill index appears before the time in the system prompt

#### Scenario: Identical prefix across turns
- **WHEN** two consecutive turns of a session differ only in time, retrieved memory and area context
- **THEN** the system prompts of both turns are identical up to the first `turn` contribution

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

