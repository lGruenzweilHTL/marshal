# conversation-api Specification

## Purpose
The public conversation API: versioning, the turn endpoint with streaming, the latency cap, moving turns to the background and follow-up replies.

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
`/health`, `/api/versions`, `/openapi/{document}.json`, `/mcp` (versioned by the MCP protocol itself), auth protocol callbacks and SPA routes SHALL be unversioned. `GET /api/versions` SHALL list supported and deprecated versions without authentication.

#### Scenario: Version discovery
- **WHEN** a client calls `GET /api/versions`
- **THEN** it receives the list of supported versions, including `1.0`

#### Scenario: MCP endpoint path
- **WHEN** a client connects to `/mcp`
- **THEN** the MCP server answers without an `/api/v{version}` prefix

### Requirement: Conversation turn endpoint
`POST /api/v1/conversation/turns` SHALL accept `conversationId`, `text`, `language`, `deviceId`, `satelliteId`, `areaId`, `haUserId`, `inputMode` (`voice` | `text`) and `followupRef`, and SHALL require the `conversation:write` scope. For token clients `conversationId` SHALL be required. An unknown `followupRef` SHALL be ignored.

#### Scenario: Missing scope
- **WHEN** a token without `conversation:write` calls the endpoint
- **THEN** the response is 403 and no run is started

#### Scenario: Valid turn
- **WHEN** a token with `conversation:write` sends a valid turn
- **THEN** the response is an SSE stream ending in a final event

### Requirement: Streaming response
The response SHALL be `text/event-stream` with `delta` events (text chunks), optional `status` events, optional `artifact` events (`{ id, name, kind, url }`, one per artifact the turn shows), optional `widget` events (`{ kind, toolCallId, data }`) and `app` events (`{ appId, server, tool, toolCallId }`) for web turns, and one final event `{ speech, continueConversation, conversationId }`. `conversationId` SHALL echo the client's id for token clients. `continueConversation` SHALL be true when Marshal asks the user something (confirmation, clarification).

#### Scenario: Confirmation question keeps listening
- **WHEN** the turn ends with a pending action
- **THEN** the final event has `continueConversation = true`

#### Scenario: Id echo
- **WHEN** a token client sends `conversationId = "01JABC"`
- **THEN** the final event carries `conversationId = "01JABC"`, not Marshal's session id

#### Scenario: Artifact shown
- **WHEN** a web turn calls `artifacts_show` for artifact `a1`
- **THEN** the stream carries an `artifact` event with `a1`'s id, name, kind and content URL before the final event

#### Scenario: Widget event
- **WHEN** a web turn calls `printer_status`
- **THEN** the stream carries a `widget` event of kind `printer` before the final event

#### Scenario: No widgets for voice
- **WHEN** a voice turn calls `printer_status`
- **THEN** the stream carries no `widget` event

### Requirement: Latency cap
The system SHALL enforce a hard latency cap per turn (default ~8 s for voice, ~60 s for text, configurable). Reaching the cap SHALL speak a short "working on it, I'll tell you when done" reply, end the turn, and continue the run as a background task owned by the same user. The original run SHALL be recorded with outcome `backgrounded`, and the task SHALL deliver its result by voice to the originating satellite (voice turns) or to the web UI (text turns). The task SHALL record the originating session. Its result SHALL be added to that session as a background result, and SHALL be part of the session's conversation history for later turns.

#### Scenario: Long voice request
- **WHEN** a voice turn exceeds the voice cap
- **THEN** the final event says Marshal is working on it, the run is recorded as `backgrounded`, and the result later arrives through notifications

#### Scenario: Result joins the session
- **WHEN** a text turn in web session S is backgrounded and its task produces the result "The printer finished at 14:02"
- **THEN** session S lists that result after the backgrounded turn, and the Inbox also receives it

#### Scenario: Follow-up question after the result
- **WHEN** the user then asks in session S "and how long did it take?"
- **THEN** the model's conversation history for that turn contains the background result as an assistant message

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

### Requirement: Reasoning and tool events for web clients
For cookie principals, the turn stream SHALL also carry `reasoning` events (`{ text }`, chunks of the model's reasoning as they arrive) and `tool` events. A `tool` event SHALL be sent when a tool call starts (`{ id, tool, arguments, state: "running" }`) and when it ends (same `id`, `state` `ok` | `error` | `denied` | `pending`, `durationMs`, optional `result`). `tool` SHALL be the tool id (for example `printer.status`), `arguments` a short one-line summary of the arguments, and `result` a short one-line summary of the result. For tools flagged sensitive or redacted by the recorder, `result` SHALL be omitted. Token principals SHALL NOT receive `reasoning` or `tool` events; their stream SHALL stay unchanged.

#### Scenario: Web turn with a tool call
- **WHEN** a cookie user's turn calls `printer_status` and then replies
- **THEN** the stream carries a `tool` event with state `running`, then one with state `ok`, then the `delta` events of the reply

#### Scenario: Home Assistant client
- **WHEN** a token client's turn uses a model that returns reasoning and calls a tool
- **THEN** the stream carries no `reasoning` and no `tool` events

#### Scenario: Sensitive tool
- **WHEN** a web turn calls a mail tool flagged sensitive
- **THEN** its ending `tool` event has no `result`

#### Scenario: Tool waits for confirmation
- **WHEN** a web turn's tool call is frozen as a pending action
- **THEN** the ending `tool` event has state `pending`

### Requirement: Spoken filler
In a turn with `inputMode = voice`, before executing a tool that declares a filler line in the turn's language and whose expected duration exceeds `voice.fillerThresholdMs` (default 700), the system SHALL send that line as a `delta` event, unless a `delta` was already sent in the turn. At most one filler SHALL be sent per turn. The expected duration SHALL be the median of the tool's recorded durations over its last 20 calls, or the threshold plus one millisecond when fewer than 5 calls are recorded. Text turns SHALL NOT get fillers. A sent filler SHALL be part of the recorded reply text.

#### Scenario: Slow tool in a voice turn
- **WHEN** a German voice turn calls `printer_snapshot`, whose median duration is 2 s, before any delta was sent
- **THEN** the stream carries the German filler as a `delta` before the tool runs, and later deltas follow it

#### Scenario: Fast tool
- **WHEN** a voice turn calls a tool whose median duration is 120 ms
- **THEN** no filler is sent

#### Scenario: Model already spoke
- **WHEN** the model streamed text before calling a slow tool
- **THEN** no filler is sent

#### Scenario: Text turn
- **WHEN** a web text turn calls a slow tool with a filler line
- **THEN** no filler is sent

### Requirement: OpenAPI document content
Each operation in a versioned OpenAPI document SHALL have its route below the version base as its summary (for `POST /api/v1/conversation/turns` the summary is `/conversation/turns`) and SHALL describe what it does in its description. The document SHALL declare the security schemes `apiToken` (HTTP bearer, a machine token) and `session` (API key in the cookie `marshal_session`). Each operation SHALL carry the security requirements its endpoint enforces:
- an anonymous endpoint SHALL have no security requirement;
- an endpoint limited to people SHALL require `session`;
- an endpoint that requires a scope SHALL accept `apiToken` with that scope or `session`;
- an endpoint for people or `admin` tokens SHALL accept `session` or `apiToken` with `admin`;
- an endpoint that requires only an authenticated caller SHALL accept `apiToken` or `session`;
- an endpoint limited to machine tokens SHALL require `apiToken`.
Where an endpoint combines several policies, it SHALL accept only credentials that every policy accepts, with the union of their scopes. Generating a document SHALL fail when an endpoint uses an authorization policy without a documented credential mapping.

#### Scenario: Operation named by route
- **WHEN** a client reads `/openapi/v1.json`
- **THEN** the operation `CreateConversationTurn` has the summary `/conversation/turns` and a description of the turn

#### Scenario: Scope-protected endpoint
- **WHEN** a client reads the operation `CreateConversationTurn`
- **THEN** its security accepts `apiToken` with the scope `conversation:write`, or `session`

#### Scenario: People-only endpoint
- **WHEN** a client reads an operation of an endpoint that requires a browser session, such as `ListApiTokens`
- **THEN** its security requires `session` only

#### Scenario: Anonymous endpoint
- **WHEN** a client reads the operation `ListLoginProviders`
- **THEN** it has no security requirement

#### Scenario: Unknown policy
- **WHEN** an endpoint requires an authorization policy that has no credential mapping and the document is generated
- **THEN** generation fails with an error naming the policy and the endpoint
