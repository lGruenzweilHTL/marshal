# skills Specification

## Purpose
Skills as trusted configuration: their format and progressive disclosure to the model.
## Requirements
### Requirement: Skill format
Skills SHALL be directories under `/config/skills/<name>/` containing `SKILL.md` with Anthropic Agent Skills frontmatter (`name`, `description`) and optional additional files. Skills SHALL be hot-reloaded. Invalid skills SHALL be rejected individually with an error.

#### Scenario: Invalid frontmatter
- **WHEN** a `SKILL.md` has no `description`
- **THEN** that skill is skipped with an error and other skills load

### Requirement: Progressive disclosure
The context SHALL contain only the index of available skills (name and description). The full skill body SHALL be loaded through `agent.load_skill(name)` (safe). Additional skill files SHALL be readable through `agent.load_skill(name, file)`. The tool SHALL be hidden when no skills are available to the current template.

#### Scenario: Index only
- **WHEN** a turn starts with three skills available
- **THEN** the system prompt lists three names and descriptions, and no skill body

#### Scenario: Load a skill
- **WHEN** the model calls `agent_load_skill` with `rss-digest`
- **THEN** the tool result contains the body of `rss-digest/SKILL.md`

### Requirement: Skills are trusted configuration
Skill files are operator-written configuration, not untrusted content. Loading a skill SHALL NOT taint the run. Skills SHALL NOT contain secrets (the config secret scanner applies).

#### Scenario: Loading does not taint
- **WHEN** a run loads a skill
- **THEN** the run's taint state is unchanged

