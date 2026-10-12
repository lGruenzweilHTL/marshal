# Agent templates (`templates/*.md`)

A template is a Markdown file in `/config/templates/` that sets what a run may see and do: model tier, tool allowlist, skills, limits and an extra prompt. Marshal reloads templates without a restart.

Conversation turns use the built-in `default` template (every tool, every skill, no extra prompt). A `default.md` file replaces it. Tasks name a template in their `template` field; `agent_delegate(template, task)` runs a subagent with one.

## Example

```markdown
---
name: house-inspector
description: Reads the current state of the house and reports what needs attention. Cannot change anything.
modelTier: fast
tools: ["ha.homeassistant__GetLiveContext", "agent.load_skill"]
skills: [house-status]
maxSteps: 4
maxAdvisorCalls: 0
---

You inspect the house on behalf of another agent. Load the house-status skill and follow it.
```

## Frontmatter

Keys are camelCase. Unknown keys are errors.

| Key | Type | Default | Description |
|---|---|---|---|
| `name` | string | required | Lower-case letters, digits and hyphens. Must equal the file name without `.md`. |
| `description` | string | required | When to use the template. Shown to callers of `agent_delegate`. |
| `modelTier` | `fast` or `standard` | by input mode (voice `fast`, otherwise `standard`) | Model tier of runs. |
| `tools` | list of globs | `[]` | Allowlist of tool id globs (for example `printer.*`, `agent.load_skill`). An empty list means no tools. |
| `skills` | list of names | all skills | Skills available to runs. `[]` means none. |
| `maxSteps` | integer 1 to 50 | `agent.maxSteps` | Step limit per run. |
| `maxAdvisorCalls` | integer 0 to 10 | `agent.advisor.maxCallsPerRun` | Advisor calls per run. |
| `minWakeupInterval` | duration | `tasks.minWakeupInterval` | Shortest time between a task run and its wakeup; at least `00:01:00`. |

The body is added to the system prompt.

## Subagent rules

Inside a subagent, confirm-tier tools are hidden and the guard denies every call that resolves to confirm, telling the subagent to report the needed action to its caller. Subagents use the caller's owner, satellite policy and taint, and pass taint back. Delegation depth is limited by `agent.maxDelegationDepth` (default 3). Templates with an empty `tools` list can still be delegated to.

## Validation

A rejected file is skipped with an error; other templates load.

- `<file>: has no YAML frontmatter (--- name / description / tools ---).`
- `<file>: name '<x>' must be lowercase letters, digits and hyphens.`
- `<file>: name '<x>' must match the file name '<file-name>'.`
- `<file>: description is required.`
- `<file>: modelTier '<x>' must be 'fast' or 'standard'.`
- `<file>: maxSteps must be between 1 and 50.`
- `<file>: maxAdvisorCalls must be between 0 and 10.`
- `<file>: minWakeupInterval must be at least 00:01:00.`
- `<file>: invalid frontmatter at line <n>: ...`
- A literal secret in the frontmatter or body.

## Related

- [Agent templates guide](../guides/agent-templates.md)
- [Skills format](skills-format.md)
- [Tool visibility](../concepts/tool-visibility.md)
