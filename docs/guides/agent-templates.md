# Agent templates

Create an agent template to restrict what a task or subagent can see and do, and delegate work to it.

## Before you start

- Access to the `/config` volume. Templates hot-reload.
- Full format reference: [templates format](../configuration/templates-format.md).

## Steps

### 1. Write the template

`/config/templates/<name>.md`: frontmatter plus a prompt body. The file name without `.md` must equal `name`.

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
Answer with facts only, in at most five short lines. Do not ask questions; nobody can answer them.
```

| Key | Description |
|---|---|
| `name`, `description` | Required. The description tells callers of `agent.delegate` when to use it. |
| `modelTier` | `fast` or `standard`. Omitted: voice turns use fast, others standard. |
| `tools` | Allowlist of tool id globs. A run sees only matching tools; `[]` means none. |
| `skills` | Skills the template may load. Omitted: all. |
| `maxSteps` | 1 to 50. Omitted: `agent.maxSteps`. |
| `maxAdvisorCalls` | 0 to 10. Omitted: `agent.advisor.maxCallsPerRun`. |
| `minWakeupInterval` | At least `00:01:00`. Shortest wakeup delay for tasks using the template. |

Unknown keys are errors. A broken template is rejected on its own: `<file>: modelTier '<x>' must be 'fast' or 'standard'.`, `maxSteps must be between 1 and 50.`, `name '<x>' must match the file name '<file>'.`

### 2. Use it as a task template

Set `template: <name>` in `tasks.create` or `tasks.update`. The run sees only the template's tools, as the task's owner, under the origin satellite's policy. The [daily briefing](tasks/daily-briefing.md) is built this way.

### 3. Delegate to it

Any run can call `agent.delegate(template, task)`. The subagent runs a nested loop with the template's prompt, tools, skills and tier, and its final text comes back as the tool result. The `task` must be complete: the subagent sees nothing else.

The nested loop:

- Uses the same guard, owner, satellite policy and taint as the caller. If the subagent reads mail, the caller is tainted afterwards.
- Never runs confirm-tier actions. Confirm-tier tools are hidden, and a call that resolves to confirm is denied with `Not done: this action needs the user's confirmation, which a subagent cannot ask for. Report the needed action to your caller in your answer.`
- Is limited in depth by `agent.maxDelegationDepth` (default 3). At the limit, or when no template is delegable, `agent.delegate` is hidden.

The built-in `default` template (every tool, every skill, no extra prompt) serves conversation turns; a `default.md` replaces it. It is not delegable.

## Check that it works

Ask Marshal to use the template ("ask the house inspector how the house is"). In **Runs**, the run shows the `agent_delegate` step and the nested steps.

## Related

- [Skills](skills.md), [daily briefing](tasks/daily-briefing.md), [risk rules](risk-rules.md)
- [Templates format](../configuration/templates-format.md), [`agent`](../configuration/marshal-yaml/agent.md)
- [Tool visibility](../concepts/tool-visibility.md)
