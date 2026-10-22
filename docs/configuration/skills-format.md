---
icon: fa-solid fa-wand-magic-sparkles
description: "A skill is a directory /config/skills/<name>/ with a SKILL.md file in the Agent Skills format, plus optional extra files."
---

# Skills (`skills/*/SKILL.md`)

A skill is a directory `/config/skills/<name>/` with a `SKILL.md` file in the Agent Skills format, plus optional extra files. Marshal reloads skills without a restart.

Skills are operator-written configuration, not untrusted content. Loading one does not taint the run. Skills must not contain secrets.

## Example

```markdown
---
name: house-status
description: "Gives a short status report of the house. Use when the user asks how the house is."
---

# House status

1. Call `ha_homeassistant__GetLiveContext` once.
2. Report only what needs attention, most important first.
```

## Frontmatter

The file must start with a `---` line, YAML, and a closing `---` line. Keys are exactly as in the table (lower-case, `allowed-tools` with a hyphen). Unknown keys are errors.

| Key | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | 1 to 64 lower-case letters, digits and single hyphens; no leading or trailing hyphen. Must equal the directory name. |
| `description` | string | yes | What the skill does and when to use it, at most 1024 characters. Shown in the skill index. |
| `license` | string | no | Accepted and ignored. |
| `compatibility` | string | no | At most 500 characters. Accepted and ignored. |
| `metadata` | map of string to string | no | Accepted and ignored. |
| `allowed-tools` | string | no | Accepted and ignored. Tool access is set by [templates](templates-format.md). |

The body (everything after the frontmatter) is the instruction text.

## Additional files

Every other file in the skill directory, including subfolders, is readable through `agent_load_skill`. Files over 256 KiB and binary files (containing a null character) are skipped. All files are scanned for literal secrets.

## How the model uses skills

The prompt holds only the index (name and description) of the skills available to the template. The model calls `agent_load_skill(name)` for the body and `agent_load_skill(name, file)` for another file. The tool is hidden when no skill is available. A template limits skills with its `skills` list ([templates](templates-format.md)).

## Validation

A rejected skill is skipped with an error; other skills load. A directory without `SKILL.md` is ignored.

- `<name>/SKILL.md: has no YAML frontmatter (--- name / description ---).`
- `<name>/SKILL.md: name is required.`
- `<name>/SKILL.md: name '<x>' must be 1-64 lowercase letters, digits and single hyphens, not starting or ending with a hyphen.`
- `<name>/SKILL.md: name '<x>' must match its directory '<dir>'.`
- `<name>/SKILL.md: description is required.`
- `<name>/SKILL.md: description must be at most 1024 characters.`
- `<name>/SKILL.md: compatibility must be at most 500 characters.`
- `<name>/SKILL.md: invalid frontmatter at line <n>: ...`
- A literal secret in the frontmatter, body or any file.

## Related

::: cards
- [Skills guide](../guides/skills.md)
- [Agent templates](templates-format.md)
:::
