## MODIFIED Requirements

### Requirement: Session lookup
Sessions from token clients SHALL be looked up by `(api_token_id, external_id)`, never by the client-supplied conversation id alone. Sessions created without a client id SHALL be looked up by Marshal's own session id plus owner. Each session SHALL record `source` (`voice` | `web` | `task` | `api` | `automation`), the creating token, identity evidence and how the owner was resolved. Sessions with source `automation` SHALL be one-shot: each AI task creates a new session and no later request continues it.

#### Scenario: Same id, different token
- **WHEN** two different tokens send the same `conversationId`
- **THEN** two separate sessions exist

#### Scenario: Continuing a session
- **WHEN** the same token sends a second turn with the same `conversationId` before the idle TTL
- **THEN** the turn is added to the existing session

#### Scenario: Automation sessions are one-shot
- **WHEN** the same token sends two AI tasks
- **THEN** two sessions with source `automation` exist
