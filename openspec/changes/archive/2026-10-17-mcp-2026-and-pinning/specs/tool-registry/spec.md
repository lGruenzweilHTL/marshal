## MODIFIED Requirements

### Requirement: Reserved namespaces
The namespaces `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music` and `system` SHALL be reserved for built-in tools. Built-in namespaces SHALL name capabilities, never providers.

#### Scenario: Reserved namespace used by another tool source
- **WHEN** a non-built-in tool source registers a tool in namespace `mail`
- **THEN** that tool source is rejected with a clear error and all other tools keep working

#### Scenario: Capability namespace for a provider integration
- **WHEN** an MCP server is configured with id `music`
- **THEN** it is rejected because `music` is reserved

### Requirement: Visibility filter
Before every model call the system SHALL remove from the tool list every tool that is forbidden, not in the active template's allowlist, denied for the satellite (`denyTools`), confirm-tier on a satellite that denies confirmation for it (`denyConfirm`), confirm-tier inside a subagent, waiting for MCP pin approval, or hidden by the tool's own visibility condition (e.g. the advisor in voice turns). Tools whose tier depends on arguments SHALL stay visible. The guard SHALL enforce the same rules on every call.

#### Scenario: Forbidden tool is invisible
- **WHEN** a risk rule marks a tool `forbidden`
- **THEN** the tool is not in the tool list, and a call to it is denied by the guard

#### Scenario: Arg-aware tool stays visible
- **WHEN** a tool is safe for some arguments and confirm for others on a satellite with `denyConfirm` for it
- **THEN** the tool is visible, and only calls that resolve to confirm are denied

#### Scenario: Tool awaiting pin approval
- **WHEN** an MCP tool's description changed and is not approved
- **THEN** the tool is not in the tool list, and a call to it is denied by the guard
