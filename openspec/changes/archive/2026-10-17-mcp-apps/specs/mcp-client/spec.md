## MODIFIED Requirements

### Requirement: MCP server configuration
MCP servers SHALL be configured in `/config/mcp/*.yaml` with `id`, transport (`stdio` with command and args, or `http` with URL for streamable HTTP), optional `toolPrefix`, optional `description` (one line used for tool retrieval), optional auth header secret reference (`authTokenSecret`), `untrusted` (default false), `untrustedTools` (optional list of globs on the tool names the server reports), `pinTools` (default true) and `apps` (default false). The server id and `toolPrefix` SHALL match `[a-z][a-z0-9]*`. A server with both `untrusted: true` and a non-empty `untrustedTools` SHALL be rejected with a config error. A server with both `untrusted: true` and `apps: true` SHALL be rejected with a config error.

#### Scenario: Invalid id
- **WHEN** a server is configured with id `home_assistant`
- **THEN** that server is rejected with a config error and other servers load

#### Scenario: Conflicting trust settings
- **WHEN** a server file sets `untrusted: true` and `untrustedTools: [todo__get_items]`
- **THEN** that server is rejected with a config error naming both keys, and other servers load

#### Scenario: Pinning off
- **WHEN** a server file sets `pinTools: false`
- **THEN** changes to that server's tools take effect without approval

#### Scenario: Apps on an untrusted server
- **WHEN** a server file sets `untrusted: true` and `apps: true`
- **THEN** that server is rejected with a config error naming both keys

## ADDED Requirements

### Requirement: App resources
For servers with `apps: true`, the client SHALL read `ui://` resources named by a tool's MCP Apps metadata with `resources/read` when the tool is called in a web turn, and SHALL cache them per server for the result's `ttlMs` (default 5 minutes). A tool's `ui://` metadata SHALL be part of its pin fingerprint.

#### Scenario: Changed UI resource reference
- **WHEN** a pinned tool's `ui://` resource URI changes
- **THEN** the tool waits for approval like any changed tool
