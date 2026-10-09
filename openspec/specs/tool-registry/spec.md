# tool-registry Specification

## Purpose
How tools are registered and shown to the model: ids and names, namespaces, descriptors, the visibility filter and external tool sources.

## Requirements

### Requirement: Internal ids and model-facing names
Every tool SHALL have an internal id `<namespace>.<name>` used in risk rules, satellite policies, template allowlists, run records and the audit log, and a model-facing name `<namespace>_<name>`. The registry SHALL map both directions. Namespaces SHALL match `[a-z][a-z0-9]*`. Built-in tool names SHALL be `snake_case`.

#### Scenario: Name mapping
- **WHEN** the model calls `agent_ask_advisor`
- **THEN** the registry resolves it to the internal id `agent.ask_advisor`

#### Scenario: Unknown model-facing name
- **WHEN** the model calls a name that is not in the current tool list
- **THEN** the call is denied, nothing is executed, and the model receives an error result

### Requirement: Reserved namespaces
The namespaces `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra` and `repos` SHALL be reserved for built-in tools. Built-in namespaces SHALL name capabilities, never providers.

#### Scenario: Reserved namespace used by another tool source
- **WHEN** a non-built-in tool source registers a tool in namespace `mail`
- **THEN** that tool source is rejected with a clear error and all other tools keep working

#### Scenario: Capability namespace for a provider integration
- **WHEN** an MCP server is configured with id `repos`
- **THEN** it is rejected because `repos` is reserved

### Requirement: Name sanitizing and collisions
Characters outside `[a-zA-Z0-9_-]` in external tool names SHALL be replaced with `_`; model-facing names longer than 64 characters SHALL be truncated with a short hash suffix. Two tools mapping to the same model-facing name SHALL cause the later tool source to be rejected.

#### Scenario: Long name
- **WHEN** an external tool's model-facing name would be 80 characters long
- **THEN** it is shortened to at most 64 characters, ending in a hash suffix, and still maps back to the original id

#### Scenario: Collision
- **WHEN** two tool sources produce the same model-facing name
- **THEN** the second source is rejected with an error naming both, and the first keeps working

### Requirement: Tool descriptors
Each tool SHALL declare its default risk tier, whether its output is untrusted (`untrustedOutput`), and whether it persists state the agent acts on later (`persists`).

#### Scenario: Descriptor flags are read by the guard
- **WHEN** a tool declared with `untrustedOutput` returns a result
- **THEN** the run is marked tainted (see `risk-guard`)

### Requirement: Visibility filter
Before every model call the system SHALL remove from the tool list every tool that is forbidden, not in the active template's allowlist, denied for the satellite (`denyTools`), confirm-tier on a satellite that denies confirmation for it (`denyConfirm`), confirm-tier inside a subagent, or hidden by the tool's own visibility condition (e.g. the advisor in voice turns). Tools whose tier depends on arguments SHALL stay visible. The guard SHALL enforce the same rules on every call.

#### Scenario: Forbidden tool is invisible
- **WHEN** a risk rule marks a tool `forbidden`
- **THEN** the tool is not in the tool list, and a call to it is denied by the guard

#### Scenario: Arg-aware tool stays visible
- **WHEN** a tool is safe for some arguments and confirm for others on a satellite with `denyConfirm` for it
- **THEN** the tool is visible, and only calls that resolve to confirm are denied

### Requirement: External tool sources
The registry SHALL accept tools from MCP servers and skills in addition to built-in tools. Each source SHALL register and unregister as a unit on reload.

#### Scenario: Source removed
- **WHEN** an MCP server file is deleted
- **THEN** all its tools disappear from the registry and the rest stay

### Requirement: Fail-closed default for external tools
External tools that no risk rule matches SHALL default to `confirm`. Built-in tools SHALL use their descriptor's default tier.

#### Scenario: Unmatched MCP tool
- **WHEN** a new MCP server's tool matches no rule in `risk.yaml`
- **THEN** calling it creates a pending action with reason `rule`
