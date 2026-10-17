## MODIFIED Requirements

### Requirement: MCP server configuration
MCP servers SHALL be configured in `/config/mcp/*.yaml` with `id`, transport (`stdio` with command and args, or `http` with URL for streamable HTTP), optional `toolPrefix`, optional `description` (one line used for tool retrieval), optional auth header secret reference (`authTokenSecret`), `untrusted` (default false), `untrustedTools` (optional list of globs on the tool names the server reports) and `pinTools` (default true). The server id and `toolPrefix` SHALL match `[a-z][a-z0-9]*`. A server with both `untrusted: true` and a non-empty `untrustedTools` SHALL be rejected with a config error.

#### Scenario: Invalid id
- **WHEN** a server is configured with id `home_assistant`
- **THEN** that server is rejected with a config error and other servers load

#### Scenario: Conflicting trust settings
- **WHEN** a server file sets `untrusted: true` and `untrustedTools: [todo__get_items]`
- **THEN** that server is rejected with a config error naming both keys, and other servers load

#### Scenario: Pinning off
- **WHEN** a server file sets `pinTools: false`
- **THEN** changes to that server's tools take effect without approval

## ADDED Requirements

### Requirement: Protocol versions
The client SHALL use MCP protocol revision 2026-07-28 with servers that support it and SHALL fall back to the newest revision a server supports among 2025-11-25 and 2025-06-18. It SHALL NOT declare sampling, roots or logging support. When a list result carries `ttlMs`, the client SHALL NOT re-list that server's tools before the time has passed unless a change notification arrives.

#### Scenario: Older server
- **WHEN** a server supports only 2025-06-18
- **THEN** the server connects and its tools are listed

#### Scenario: No deprecated capabilities
- **WHEN** the client connects to any server
- **THEN** it declares no sampling, roots or logging capability

### Requirement: Tool pinning
For servers with `pinTools: true`, the system SHALL store a fingerprint of each tool (SHA-256 over the canonical JSON of name, title, description, input schema, output schema and annotations). On a server's first successful discovery, when no pins exist for its id, all its tools SHALL be pinned as approved. On later discoveries, a tool without a pin (`new`) or with a different fingerprint (`changed`) SHALL wait for approval: it SHALL be hidden and denied, while the server's unchanged tools keep working. A tool that disappears SHALL keep its pin; if it returns unchanged, it SHALL be approved again. Each new waiting tool SHALL raise the health warning `mcp.tool_pending` and SHALL notify the instance owner with priority `normal`.

#### Scenario: First connect
- **WHEN** server `paperless` is discovered for the first time with five tools
- **THEN** all five tools are pinned as approved and visible

#### Scenario: Changed description
- **WHEN** `paperless.search` later reports a different description
- **THEN** `paperless_search` is hidden, the other four tools stay visible, a health warning appears and the owner is notified

#### Scenario: Schema change
- **WHEN** a tool's input schema gains a property
- **THEN** the tool waits for approval like a changed description

### Requirement: Server tasks
The client SHALL declare the tasks extension. When a tool call returns a task handle, the system SHALL create a waiting agent task (owner, origin session and satellite, goal naming the server and tool) and return a tool result saying the work continues in the background. The system SHALL poll the server's task status (respecting the server's poll interval, at least every 5 seconds and at most every 5 minutes with backoff) and, when the task completes, fails or is cancelled, SHALL resume the waiting task with the outcome as an untrusted or trusted tool result according to the tool's trust. Input requests during a server task SHALL be handled as pending input requests. Cancelling the Marshal task SHALL cancel the server task when the server supports it.

#### Scenario: Long export
- **WHEN** a server's `export_report` call returns a task handle
- **THEN** the turn's reply says the export continues in the background, and when the server reports completion the waiting task runs and delivers the result through its deliveries

#### Scenario: Server task fails
- **WHEN** the server reports the task failed
- **THEN** the waiting task resumes with the failure and delivers a short message saying so
