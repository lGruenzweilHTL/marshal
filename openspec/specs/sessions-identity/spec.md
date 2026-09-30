# sessions-identity Specification

## Purpose
Conversation sessions and who they belong to: session lookup, the owner pinned on the first turn, the execution context and idle close.
## Requirements
### Requirement: Session lookup
Sessions from token clients SHALL be looked up by `(api_token_id, external_id)`, never by the client-supplied conversation id alone. Sessions created without a client id SHALL be looked up by Marshal's own session id plus owner. Each session SHALL record `source` (`voice` | `web` | `task` | `api`), the creating token, identity evidence and how the owner was resolved.

#### Scenario: Same id, different token
- **WHEN** two different tokens send the same `conversationId`
- **THEN** two separate sessions exist

#### Scenario: Continuing a session
- **WHEN** the same token sends a second turn with the same `conversationId` before the idle TTL
- **THEN** the turn is added to the existing session

### Requirement: Owner pinned on first turn
On the first turn of a new session the system SHALL call `IIdentityResolver` with `haUserId`, `deviceId` and `satelliteId`, store the owner and the evidence, and never re-resolve it for that session. In v1 the resolver SHALL always return the instance owner.

#### Scenario: v1 resolver
- **WHEN** a new session starts with any evidence
- **THEN** the owner is the instance owner and `resolved_by` is `owner_default`

### Requirement: Execution context
Every run SHALL carry an execution context `{ OwnerId, Source, InputMode, Satellite, Language, Taint }`. Tools, the guard and data access SHALL read the user from it, never from a global.

#### Scenario: Tool sees the owner
- **WHEN** a tool executes in a run for owner A
- **THEN** it can only read and write owner A's data

### Requirement: Per-turn data on the run
Per-turn data (`inputMode`, satellite, area, language) SHALL be stored on the run, not on the session.

#### Scenario: Mixed input modes
- **WHEN** one session receives a voice turn and then a text turn
- **THEN** the two runs record different `input_mode` values and the session has none

### Requirement: Idle close
A session SHALL be closed after a configurable idle TTL (a few minutes). A later turn with the same client id SHALL start a new session with a new owner resolution.

#### Scenario: Turn after idle TTL
- **WHEN** a turn arrives after the session's idle TTL expired
- **THEN** a new session is created and any pending action of the old session is dropped

