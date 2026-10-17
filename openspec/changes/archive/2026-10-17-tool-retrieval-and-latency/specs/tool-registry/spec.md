## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Tool descriptors
Each tool SHALL declare its default risk tier, whether its output is untrusted (`untrustedOutput`), and whether it persists state the agent acts on later (`persists`). A built-in tool MAY declare a short filler line per supported language (German and English) that is spoken while it runs.

#### Scenario: Descriptor flags are read by the guard
- **WHEN** a tool declared with `untrustedOutput` returns a result
- **THEN** the run is marked tainted (see `risk-guard`)

#### Scenario: Filler line declared
- **WHEN** `printer.snapshot` declares the fillers "Ich schaue mir den Drucker an." and "Let me look at the printer."
- **THEN** a German voice turn that calls it can speak the German line
