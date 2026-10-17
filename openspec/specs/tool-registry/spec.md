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
The namespaces `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music` and `system` SHALL be reserved for built-in tools. Built-in namespaces SHALL name capabilities, never providers.

#### Scenario: Reserved namespace used by another tool source
- **WHEN** a non-built-in tool source registers a tool in namespace `mail`
- **THEN** that tool source is rejected with a clear error and all other tools keep working

#### Scenario: Capability namespace for a provider integration
- **WHEN** an MCP server is configured with id `music`
- **THEN** it is rejected because `music` is reserved

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

### Requirement: Namespace descriptions
Every tool namespace SHALL have a one-line English description used for tool retrieval. Built-in tool sources SHALL declare the description of their namespaces. An MCP server MAY set `description` in its `/config/mcp/<id>.yaml`; without it, the description SHALL be built from the server id and the names of its tools.

#### Scenario: MCP description from config
- **WHEN** `/config/mcp/paperless.yaml` sets `description: "Scanned documents, letters and bills"`
- **THEN** retrieval matches the `paperless` namespace against that text

#### Scenario: MCP description fallback
- **WHEN** an MCP server has no `description`
- **THEN** its namespace description is built from its id and tool names

### Requirement: Tool retrieval
After the visibility filter, the system SHALL reduce the tool list to the loaded namespaces of the session. The namespaces in `tools.core` (default `agent`, `memory`, `tasks`, `notifications`, `timers`, `places`) SHALL always be loaded. Before each turn's first model call, the system SHALL load further namespaces whose description matches the turn text above `tools.retrieval.threshold` (at most `tools.retrieval.maxPerTurn`, default 3, best first) and every namespace a deterministic hint names. Loaded namespaces SHALL stay loaded for the rest of the session. Retrieval SHALL be skipped, and all visible tools shown, when `tools.retrieval.enabled` is false or the visible tool list has at most `tools.retrieval.minTools` tools (default 24). Retrieval SHALL never make a tool visible that the visibility filter removed.

#### Scenario: Printer question
- **WHEN** a session has only the core namespaces loaded and the turn is "how far is the print?"
- **THEN** the `printer` namespace is loaded and the model call includes the printer tools

#### Scenario: Loaded namespaces persist
- **WHEN** the first turn loaded `music` and the second turn is "a bit louder"
- **THEN** the second turn's model call still includes the music tools

#### Scenario: Entity hint
- **WHEN** the turn names the HA entity "Stehlampe" and its embedding does not match the `ha` description
- **THEN** the `ha` namespace is loaded by the HA hint

#### Scenario: Narrow template
- **WHEN** a run's template allows only two tools
- **THEN** retrieval is skipped and both tools are visible

#### Scenario: Filtered tool stays hidden
- **WHEN** a satellite denies `mail.*` and the turn is about mail
- **THEN** no mail tool is in the list, whatever the retrieval score

### Requirement: Finding tools
The system SHALL provide `agent.find_tools(query)` (safe). It SHALL load the namespaces whose description best matches `query` (at most `tools.retrieval.maxPerTurn`), considering only namespaces with at least one tool that is visible in this run, and SHALL return their names and descriptions. Newly loaded tools SHALL be offered from the next model call of the same run on. The tool SHALL be hidden when retrieval is disabled or skipped for the run, or when every namespace with visible tools is already loaded. A call to a visible tool whose namespace is not loaded SHALL load the namespace and proceed through the guard as usual.

#### Scenario: Model finds a missing namespace
- **WHEN** the model calls `agent_find_tools` with "calendar"
- **THEN** the result names `calendar` and the next model call includes the calendar tools

#### Scenario: Hidden namespaces are not revealed
- **WHEN** a satellite denies `mail.*` and the model calls `agent_find_tools` with "mail"
- **THEN** the result does not name `mail`

#### Scenario: Call to an unloaded namespace
- **WHEN** the model calls `printer_status`, which is visible but not loaded
- **THEN** `printer` is loaded and the call is executed after the guard allows it

### Requirement: Stable tool order
The tool list SHALL be ordered deterministically: core namespaces in the order of `tools.core`, then other namespaces in the order they were loaded in the session, and tools within a namespace by name. Loading a namespace SHALL only append tools to the end of the list.

#### Scenario: Append only
- **WHEN** a session's list is core plus `printer` and the turn loads `music`
- **THEN** the new list starts with exactly the previous list and ends with the music tools
