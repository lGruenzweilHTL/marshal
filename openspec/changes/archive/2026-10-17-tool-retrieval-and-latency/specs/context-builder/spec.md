## MODIFIED Requirements

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
