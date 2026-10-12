# Spec Delta

## MODIFIED Requirements

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
