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
A session from a token client (source `voice` or `api`) SHALL be closed after a configurable idle TTL (a few minutes). A later turn with the same client id SHALL start a new session with a new owner resolution. A session with source `web` SHALL NOT be closed by the idle TTL. When the idle TTL passes, a web session SHALL instead rest: its pending actions are dropped, its project activation is forgotten and its new turns are summarized into memory, exactly as on close. A later turn with the session id of a rested web session SHALL continue that session, whatever time has passed, and SHALL make it active again.

#### Scenario: Turn after idle TTL
- **WHEN** a token client's turn arrives after the session's idle TTL expired
- **THEN** a new session is created and any pending action of the old session is dropped

#### Scenario: Web session after a long pause
- **WHEN** a cookie user sends a turn with the id of their web session that was last used two days ago
- **THEN** the turn is added to that session and the conversation history of the session is used

#### Scenario: Rested web session drops its pending action
- **WHEN** a web session with a pending action rests after the idle TTL and the user later answers "yes" in it
- **THEN** the dropped action does not execute and the answer is handled as an ordinary turn
