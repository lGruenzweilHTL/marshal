## ADDED Requirements

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
The system SHALL enforce a hard latency cap per turn (default ~8 s for voice, ~60 s for text, configurable). Reaching the cap SHALL stream a `status` event, cancel the run, record outcome `cancelled` with reason `latency_cap`, and end with a short reply that the request takes too long.

#### Scenario: Slow model
- **WHEN** a voice turn takes longer than the voice cap
- **THEN** the stream ends with the "this takes too long" reply and no further tool calls are executed

### Requirement: Health endpoint
`/health` SHALL report liveness and readiness (database reachable, required secrets resolved, config loaded) without authentication and without secret values.

#### Scenario: Missing optional secret
- **WHEN** an optional secret reference does not resolve
- **THEN** `/health` reports a warning naming the reference, not its value
