## MODIFIED Requirements

### Requirement: Conversation list
`GET /api/v1/conversations` SHALL return the current user's sessions with source `web`, `voice` or `api`, ordered by last turn time, newest first, paged by a cursor. Sessions with source `task` or `automation` SHALL NOT be listed. Each entry SHALL carry the session id, source, created and last-turn times, the number of turns, a title (the first user input, cut to 80 characters), whether the conversation can be continued (`web` only), and for voice sessions the satellite and area of the first turn. The endpoint SHALL require a human principal and SHALL only return sessions of the current owner.

#### Scenario: Newest first
- **WHEN** the user has a web session last used yesterday and a voice session last used an hour ago
- **THEN** the voice session is listed first

#### Scenario: Task sessions hidden
- **WHEN** a scheduled task run created a session with source `task`
- **THEN** that session is not in the list

#### Scenario: Automation sessions hidden
- **WHEN** an AI task created a session with source `automation`
- **THEN** that session is not in the list

#### Scenario: Other owner
- **WHEN** owner B has sessions and owner A requests the list
- **THEN** none of B's sessions are returned

#### Scenario: Token principal
- **WHEN** a machine token calls `GET /api/v1/conversations`
- **THEN** the response is 403
