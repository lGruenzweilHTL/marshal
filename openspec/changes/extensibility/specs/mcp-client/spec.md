## ADDED Requirements

### Requirement: MCP server configuration
MCP servers SHALL be configured in `/config/mcp/*.yaml` with `id`, transport (`stdio` with command and args, or `http` with URL for streamable HTTP), optional `toolPrefix`, optional auth header secret reference (`authTokenSecret`) and `untrusted` (default false). The server id and `toolPrefix` SHALL match `[a-z][a-z0-9]*`.

#### Scenario: Invalid id
- **WHEN** a server is configured with id `home_assistant`
- **THEN** that server is rejected with a config error and other servers load

### Requirement: Flat namespaces
Tools from an MCP server SHALL use the server id (or `toolPrefix`) as namespace, without an `mcp.` prefix. Tool names SHALL be kept as reported, sanitized per the registry rules.

#### Scenario: HA tool id
- **WHEN** server `ha` reports tool `intent__HassTurnOn`
- **THEN** its internal id is `ha.intent__HassTurnOn` and its model-facing name is `ha_intent__HassTurnOn`

### Requirement: Failure isolation
A server that fails to start, fails discovery, collides with a reserved or existing namespace, or produces colliding tool names SHALL be rejected with a clear error; all other servers and built-in tools SHALL keep working. A server that disconnects SHALL be reconnected with backoff; its tools SHALL be hidden while it is down.

#### Scenario: Server down
- **WHEN** a configured server is unreachable
- **THEN** its tools are not in the tool list, `/health` shows a warning, and other tools work

#### Scenario: Namespace collision
- **WHEN** a server is configured with id `mail`
- **THEN** it is rejected because `mail` is reserved

### Requirement: Hot reload
Adding, changing or removing a server file SHALL take effect without restart; tool lists of running turns SHALL NOT change mid-turn.

#### Scenario: New server added
- **WHEN** a new server file is added
- **THEN** its tools appear in the tool list of the next turn

### Requirement: Untrusted servers taint runs
Tools of a server configured with `untrusted: true` SHALL have `untrustedOutput`, so their results taint the run and name the server as taint source.

#### Scenario: Web search server
- **WHEN** a run calls a tool of an untrusted server
- **THEN** the run is tainted with source `mcp:<id>`
