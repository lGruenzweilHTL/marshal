## MODIFIED Requirements

### Requirement: Unversioned endpoints
`/health`, `/api/versions`, `/openapi/{document}.json`, `/mcp` (versioned by the MCP protocol itself), auth protocol callbacks and SPA routes SHALL be unversioned. `GET /api/versions` SHALL list supported and deprecated versions without authentication.

#### Scenario: Version discovery
- **WHEN** a client calls `GET /api/versions`
- **THEN** it receives the list of supported versions, including `1.0`

#### Scenario: MCP endpoint path
- **WHEN** a client connects to `/mcp`
- **THEN** the MCP server answers without an `/api/v{version}` prefix
