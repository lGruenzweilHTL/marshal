## MODIFIED Requirements

### Requirement: Reserved namespaces
The namespaces `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra` and `repos` SHALL be reserved for built-in tools. Built-in namespaces SHALL name capabilities, never providers.

#### Scenario: Reserved namespace used by another tool source
- **WHEN** a non-built-in tool source registers a tool in namespace `mail`
- **THEN** that tool source is rejected with a clear error and all other tools keep working

#### Scenario: Capability namespace for a provider integration
- **WHEN** an MCP server is configured with id `repos`
- **THEN** it is rejected because `repos` is reserved
