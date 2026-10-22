---
icon: fa-solid fa-wand-magic-sparkles
description: "Write a skill to teach Marshal a procedure, and make it available to the right agent templates."
---

# Skills

Write a skill to teach Marshal a procedure, and make it available to the right agent templates.

## Before you start

- Access to the `/config` volume. Skills are trusted configuration written by you; loading one never taints a run.
- Full format reference: [skills format](../configuration/skills-format.md).

## Steps

::: steps
1. **Create the directory and `SKILL.md`**

   A skill is a directory `/config/skills/<name>/` with a `SKILL.md`.

   ```markdown
   ---
   name: house-status
   description: Reports what in the house needs attention. Use when asked how the house is doing.
   ---

   # House status

   1. Call `ha_homeassistant__GetLiveContext`.
   2. List open windows, low batteries and finished appliances in at most five short lines.
   ```

   Rules checked on load:

   | Rule | Error text (prefixed with `<name>/SKILL.md:`) |
   |---|---|
   | Frontmatter present | `has no YAML frontmatter (--- name / description ---).` |
   | `name` set, 1 to 64 lowercase letters, digits and single hyphens | `name is required.` / `name '<x>' must be 1-64 lowercase letters, digits and single hyphens, not starting or ending with a hyphen.` |
   | `name` equals the directory name | `name '<x>' must match its directory '<dir>'.` |
   | `description` set, at most 1024 characters | `description is required.` |
   | No unknown frontmatter keys | `invalid frontmatter at line <n>: ...` |
   | No secrets | the secret scanner rejects keys and values that look like credentials |

   Optional frontmatter keys: `license`, `compatibility` (at most 500 characters), `metadata` and `allowed-tools`. Put the trigger in the `description`: the model sees only name and description until it loads the skill.

2. **Add extra files (optional)**

   Other text files in the skill directory (up to 256 KB each) are loaded on demand with `agent_load_skill` and a `file` argument, for example `references/format.md`. The tool result lists them.

3. **Make it available**

   A template lists the skills it can use in its `skills` key; without `skills`, a run sees all skills. The `default` template (conversation turns) sees all. For a template with an allowlist, add the skill name and the tool `agent.load_skill` to its `tools`, see [agent templates](agent-templates.md).

   Marshal puts only the index (name and description) in the prompt and the model loads the body when it needs it. `agent.load_skill` is hidden when no skill is available to the current template.

4. **Reload**

   Skills hot-reload. An invalid skill is skipped with an error in the log and in the configuration view; other skills still load.
:::

## Check that it works

Open the configuration view in the web UI (**System**, then configuration) for rejected skills, then ask Marshal something the skill covers and open the run: you see an `agent_load_skill` tool step.

## Troubleshooting

- Skill not offered: the template's `skills` list omits it, or it was rejected. See [troubleshooting](troubleshooting.md).
- `There is no skill '<name>'. Available: ...`: the name is wrong or the template does not allow the skill.

## Related

::: cards
- [Agent templates](agent-templates.md)
- [Daily briefing](tasks/daily-briefing.md)
- [Skills format](../configuration/skills-format.md)
:::
