# conversation-api Specification

## Purpose
TBD - created by archiving change core-agent. Update Purpose after archive.
## Requirements
### Requirement: Versioned public API
The public API SHALL be versioned by URL segment (`/api/v{version}`) with `Asp.Versioning.Http`. Versions SHALL be declared once in code (`ApiVersions.V1`) and never read from configuration. Endpoint files SHALL contain no version literals. Each version SHALL have its own OpenAPI document (`/openapi/v1.json`). JSON SHALL be camelCase.

#### Scenario: Versioned route
- **WHEN** a client calls `POST /api/v1/conversation/turns`
- **THEN** the request is handled by the v1 endpoint and the response carries `api-supported-versions`

#### Scenario: Unsupported version
- **WHEN** a client calls `/api/v9/conversation/turns`
- **THEN** the response is an error naming the supported versions

### Requirement: Unversioned endpoints
`/health`, `/api/versions`, `/openapi/{document}.json`, auth protocol callbacks and SPA routes SHALL be unversioned. `GET /api/versions` SHALL list supported and deprecated versions without authentication.

#### Scenario: Version discovery
- **WHEN** a client calls `GET /api/versions`
- **THEN** it receives the list of supported versions, including `1.0`

### Requirement: Conversation turn endpoint
`POST /api/v1/conversation/turns` SHALL accept `conversationId`, `text`, `language`, `deviceId`, `satelliteId`, `areaId`, `haUserId`, `inputMode` (`voice` | `text`) and `followupRef`, and SHALL require the `conversation:write` scope. For token clients `conversationId` SHALL be required. An unknown `followupRef` SHALL be ignored.

#### Scenario: Missing scope
- **WHEN** a token without `conversation:write` calls the endpoint
- **THEN** the response is 403 and no run is started

#### Scenario: Valid turn
- **WHEN** a token with `conversation:write` sends a valid turn
- **THEN** the response is an SSE stream ending in a final event

### Requirement: Streaming response
The response SHALL be `text/event-stream` with `delta` events (text chunks), optional `status` events, and one final event `{ speech, continueConversation, conversationId }`. `conversationId` SHALL echo the client's id for token clients. `continueConversation` SHALL be true when Marshal asks the user something (confirmation, clarification).

#### Scenario: Confirmation question keeps listening
- **WHEN** the turn ends with a pending action
- **THEN** the final event has `continueConversation = true`

#### Scenario: Id echo
- **WHEN** a token client sends `conversationId = "01JABC"`
- **THEN** the final event carries `conversationId = "01JABC"`, not Marshal's session id

### Requirement: Latency cap
The system SHALL enforce a hard latency cap per turn (default ~8 s for voice, ~60 s for text, configurable). Reaching the cap SHALL speak a short "working on it, I'll tell you when done" reply, end the turn, and continue the run as a background task owned by the same user. The original run SHALL be recorded with outcome `backgrounded`, and the task SHALL deliver its result by voice to the originating satellite (voice turns) or to the web UI (text turns).

#### Scenario: Long voice request
- **WHEN** a voice turn exceeds the voice cap
- **THEN** the final event says Marshal is working on it, the run is recorded as `backgrounded`, and the result later arrives through notifications

### Requirement: Health endpoint
`/health` SHALL report liveness and readiness (database reachable, required secrets resolved, config loaded) without authentication and without secret values.

#### Scenario: Missing optional secret
- **WHEN** an optional secret reference does not resolve
- **THEN** `/health` reports a warning naming the reference, not its value

### Requirement: Cookie clients on the turn endpoint
`POST /api/v1/conversation/turns` SHALL accept cookie principals. For cookie principals `conversationId` SHALL be optional: omitted creates a new session with source `web`; otherwise it SHALL be Marshal's own session id and SHALL belong to the current user. The final event SHALL return Marshal's session id.

#### Scenario: Foreign session id
- **WHEN** a cookie user sends another user's session id
- **THEN** the response is 404 and no run starts

#### Scenario: New web session
- **WHEN** a cookie user sends a turn without `conversationId`
- **THEN** a session with source `web` is created and its id is in the final event

### Requirement: Role-derived scopes
A cookie principal SHALL carry the scopes its roles grant: `user` → all non-admin scopes, `admin` → all scopes. Cookie and token principals SHALL be authorized by the same policies.

#### Scenario: User role cannot use admin endpoints
- **WHEN** a cookie user with role `user` calls an endpoint requiring `admin`
- **THEN** the response is 403

### Requirement: Self-backgrounding
The system SHALL provide `agent.move_to_background(statusMessage)` (safe), visible only in interactive turns. Calling it SHALL speak the status message and continue the run as a background task, like the latency cap.

#### Scenario: Model backgrounds early
- **WHEN** the model calls `agent_move_to_background` with "I'll look into it"
- **THEN** the final event contains that message and a background task continues the work

### Requirement: Background rules after the switch
On any switch to background, the tool list SHALL be recomputed with background rules (advisor visible, confirm-tier → approval queue), and the originating satellite's `denyTools` / `denyConfirm` SHALL keep applying through the task's `origin_satellite_id`.

#### Scenario: Policy kept
- **WHEN** a turn from a satellite with `denyConfirm: [printer.*]` is backgrounded
- **THEN** the background run cannot queue a printer approval

### Requirement: followupRef handling
A turn's `followupRef` SHALL be resolved as defined in `voice-followup`.

#### Scenario: Valid reference
- **WHEN** a turn carries a valid `followupRef` for a pending action
- **THEN** the reply enters the confirmation flow of that action

