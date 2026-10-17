## MODIFIED Requirements

### Requirement: Visibility filter
Before every model call the system SHALL remove from the tool list every tool that is forbidden, not in the active template's allowlist, outside the tool policy of the task the run belongs to, denied for the satellite (`denyTools`), confirm-tier on a satellite that denies confirmation for it (`denyConfirm`), confirm-tier inside a subagent, waiting for MCP pin approval, or hidden by the tool's own visibility condition (e.g. the advisor in voice turns). Tools whose tier depends on arguments SHALL stay visible. The guard SHALL enforce the same rules on every call.

#### Scenario: Forbidden tool is invisible
- **WHEN** a risk rule marks a tool `forbidden`
- **THEN** the tool is not in the tool list, and a call to it is denied by the guard

#### Scenario: Arg-aware tool stays visible
- **WHEN** a tool is safe for some arguments and confirm for others on a satellite with `denyConfirm` for it
- **THEN** the tool is visible, and only calls that resolve to confirm are denied

#### Scenario: Tool awaiting pin approval
- **WHEN** an MCP tool's description changed and is not approved
- **THEN** the tool is not in the tool list, and a call to it is denied by the guard

#### Scenario: Outside the task policy
- **WHEN** a task's policy contains only printer tools and its run reads injected text asking to call `mail_draft`
- **THEN** `mail_draft` is not in the tool list, and a call to it is denied by the guard
