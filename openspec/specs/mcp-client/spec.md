# mcp-client Specification

## Purpose
Tools from Model Context Protocol servers: configuration, namespaces, failure isolation, hot reload, and taint from untrusted servers.

## Requirements

### Requirement: MCP server configuration
MCP servers SHALL be configured in `/config/mcp/*.yaml` with `id`, transport (`stdio` with command and args, or `http` with URL for streamable HTTP), optional `toolPrefix`, optional auth header secret reference (`authTokenSecret`), `untrusted` (default false) and `untrustedTools` (optional list of globs on the tool names the server reports). The server id and `toolPrefix` SHALL match `[a-z][a-z0-9]*`. A server with both `untrusted: true` and a non-empty `untrustedTools` SHALL be rejected with a config error.

#### Scenario: Invalid id
- **WHEN** a server is configured with id `home_assistant`
- **THEN** that server is rejected with a config error and other servers load

#### Scenario: Conflicting trust settings
- **WHEN** a server file sets `untrusted: true` and `untrustedTools: [todo__get_items]`
- **THEN** that server is rejected with a config error naming both keys, and other servers load

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
Tools of a server configured with `untrusted: true`, and tools whose reported name matches a glob in the server's `untrustedTools`, SHALL have `untrustedOutput`, so their results taint the run and name the server as taint source. Other tools of the server SHALL NOT taint the run. A glob in `untrustedTools` that matches none of the server's tools after discovery SHALL be shown as a warning for that server on the config view and `/health`.

#### Scenario: Web search server
- **WHEN** a run calls a tool of an untrusted server
- **THEN** the run is tainted with source `mcp:<id>`

#### Scenario: Reading a shared to-do list
- **WHEN** server `ha` has `untrustedTools: [todo__get_items]` and a run calls `ha_todo__get_items`
- **THEN** the result is wrapped as untrusted data and the run is tainted with source `mcp:ha`

#### Scenario: Home control stays trusted
- **WHEN** server `ha` has `untrustedTools: [todo__get_items]` and a run calls `ha_intent__HassTurnOn`
- **THEN** the run is not tainted

#### Scenario: Glob matches no tool
- **WHEN** server `ha` has `untrustedTools: [todo__get_itemz]` and reports no tool with that name
- **THEN** the server's tools load, and the config view and `/health` show a warning naming the glob

#### Scenario: Hot reload of untrusted tools
- **WHEN** `untrustedTools` is added to a running server's file
- **THEN** the next turn's tools of that server are marked according to the new list
