## MODIFIED Requirements

### Requirement: MCP server configuration
MCP servers SHALL be configured in `/config/mcp/*.yaml` with `id`, transport (`stdio` with command and args, or `http` with URL for streamable HTTP), optional `toolPrefix`, optional auth header secret reference (`authTokenSecret`), `untrusted` (default false) and `untrustedTools` (optional list of globs on the tool names the server reports). The server id and `toolPrefix` SHALL match `[a-z][a-z0-9]*`. A server with both `untrusted: true` and a non-empty `untrustedTools` SHALL be rejected with a config error.

#### Scenario: Invalid id
- **WHEN** a server is configured with id `home_assistant`
- **THEN** that server is rejected with a config error and other servers load

#### Scenario: Conflicting trust settings
- **WHEN** a server file sets `untrusted: true` and `untrustedTools: [todo__get_items]`
- **THEN** that server is rejected with a config error naming both keys, and other servers load

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
