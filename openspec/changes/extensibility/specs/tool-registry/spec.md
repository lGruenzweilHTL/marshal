## ADDED Requirements

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
