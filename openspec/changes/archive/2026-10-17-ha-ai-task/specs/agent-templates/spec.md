## MODIFIED Requirements

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
