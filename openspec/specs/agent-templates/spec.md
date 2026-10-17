# agent-templates Specification

## Purpose
Agent templates and subagents: their format, and the rule that subagents never run confirm-tier actions.
## Requirements
### Requirement: Template format
Agent templates SHALL be Markdown files in `/config/templates/` with frontmatter `name`, `description`, `modelTier`, `tools` (allowlist of tool id globs), optional `skills`, optional `maxSteps` and `maxAdvisorCalls`, and a prompt body. Templates SHALL be hot-reloaded. A built-in `default` template SHALL be used for conversation turns unless configured otherwise. A built-in `ai_task` template SHALL be used for AI task runs; it SHALL allow no tools, and a file `/config/templates/ai_task.md` SHALL replace it.

#### Scenario: Allowlist
- **WHEN** a template's `tools` is `[printer.*, agent.load_skill]`
- **THEN** runs with that template see only matching tools

#### Scenario: Built-in AI task template
- **WHEN** no `/config/templates/ai_task.md` exists and an AI task runs
- **THEN** the run sees no tools

#### Scenario: Configured AI task template
- **WHEN** `/config/templates/ai_task.md` allows `calendar.list_events`
- **THEN** AI task runs see `calendar_list_events`

### Requirement: Subagents
The system SHALL provide `agent.delegate(template, task)` (safe) that runs a nested agent loop with the template's prompt, tools, skills and model tier, and returns its final text as the tool result. The nested loop SHALL use the same guard, the caller's owner, satellite policy and taint, and SHALL pass its taint back to the caller. Delegation depth SHALL be limited (default 3). The tool SHALL be hidden when no template is delegable or the depth limit is reached.

#### Scenario: Delegation returns inline
- **WHEN** the model delegates to `rss-scorer` and the subagent answers "3 relevant items"
- **THEN** the caller receives "3 relevant items" as the tool result in the same run

#### Scenario: Depth limit
- **WHEN** a subagent at depth 3 runs
- **THEN** `agent_delegate` is not in its tool list

#### Scenario: Taint flows back
- **WHEN** a subagent reads mail
- **THEN** the calling run is tainted after the delegation returns

### Requirement: Subagents never run confirm-tier actions
Inside a subagent, confirm-tier tools SHALL be hidden, arg-aware tools SHALL stay visible, and the guard SHALL deny every call that resolves to confirm with an error telling the subagent to report the needed action to its caller.

#### Scenario: Subagent tries to unlock
- **WHEN** a subagent calls an HA tool that resolves to a lock
- **THEN** the call is denied, no pending action is created, and the subagent receives the denial message

