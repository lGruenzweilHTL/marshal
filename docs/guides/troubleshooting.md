---
icon: fa-solid fa-stethoscope
---

# Troubleshooting

Find the cause of a problem from what you see, using the messages Marshal prints.

## Before you start

- Read the container log: `docker compose logs marshal`.
- Check `GET /health` (unauthenticated) and the **System** page. See [observability](observability.md).
- Raise `logging.logLevel.default` to `Debug` for stack traces on configuration errors.

## Startup fails

Startup checks in stages (configuration, database, secrets, services) and runs a stage only when the earlier ones found no errors. Configuration and secret errors are printed as one line each, without values, and the process exits with code `1`:

```text
marshal: startup failed (2 errors)
  marshal.yaml  embedding.model  is required.
  risk.yaml  risk[1].tier must be safe, confirm or forbidden.
```

| Symptom | Cause | Fix |
|---|---|---|
| `marshal.yaml` error with a line and column, reported alone | The YAML cannot be parsed. | Fix the syntax at that position. Nothing else is checked until it parses. |
| `<key> is required.` / `<model>.provider must be one of ...` / `models.<name>.apiKeySecret is required for anthropic models.` / `...endpoint is required for openai-compatible models.` / `embedding.model is required.` | Missing or invalid option. | Set the key named in the message, see [`models`](../configuration/marshal-yaml/models.md) and [`embedding`](../configuration/marshal-yaml/embedding.md). |
| The error names an environment variable such as `MARSHAL__Embedding__Provider` | The invalid value comes from the environment, which overrides the file. | Fix or remove the variable. |
| `'<key>' must hold a secret name (^[a-z][a-z0-9_]{0,63}$), not a value.` | A `...Secret` key holds a value, or a value that looks like one. | Put the value into the secret store and reference it by name. A 32-character hex string fails even as a name. |
| `'<key>' names a credential. Store the value as a secret and reference it with a '...Secret' key.` | A key such as `password`, `token` or `apiKey` in a config file. | Rename to `<thing>Secret: <name>`. `maxTokens` is fine. |
| `the value of '<key>' looks like a credential.` | A PEM header, JWT, `sk-`, `ghp_`, `xox?-` or other long high-entropy value. | Move it to a secret. See [secrets reference](../configuration/secrets-reference.md). |
| `<reference> does not resolve.` (all missing secrets listed at once) | A required secret has no value in `/run/secrets`, the environment or the database. | Create `/run/secrets/<name>` or set `MARSHAL_SECRET_<NAME>`, see [secrets](secrets.md). |
| `auth.providers allows only one 'local' provider.` / `auth.providers uses the name '<x>' more than once.` | Invalid `auth` section. | Fix [`auth`](../configuration/marshal-yaml/auth.md). |
| `owner.language is invalid.` | Language is not `de` or `en`. | Set `owner.language`. |
| One-line summary plus a stack trace | An unexpected failure, for example the HTTP port is in use. | Read the summary; free the port or fix the mount. |

## Configuration problems after startup

| Symptom | Cause | Fix |
|---|---|---|
| Edited `risk.yaml`, rules unchanged | The reload failed validation; the previous rules stay active. | Read the log line, for example `risk.yaml: risk[2].match.tool is required and must be a glob string.`, and fix it. |
| A tool Marshal should use is missing from its tool list | A `forbidden` rule, a template allowlist, a satellite `denyTools`, or the integration is not configured (tools are hidden until set). | Check [risk rules](risk-rules.md), the template `tools` list and the integration's settings. Hidden tools are not shown to the model at all. |
| Skill not offered; log says `Skill <name>/SKILL.md rejected: ...` | Invalid frontmatter or a name that differs from the directory. | See [skills](skills.md). Other skills still load. |
| Log says `Template <file> rejected: ...` | Invalid template frontmatter. | See [agent templates](agent-templates.md). |
| `MCP server file <file> rejected: ...` or `<file>: <key> (<secret>) does not resolve.` | Bad MCP server file or missing secret. | Fix the file, see [MCP servers](../configuration/mcp-servers.md). |
| Model or auth changes ignored | They need a restart. | Restart the container. Risk rules, satellites, skills, templates and MCP servers hot-reload. |

## Confirmation and tasks

| Symptom | Cause | Fix |
|---|---|---|
| Marshal asks for confirmation unexpectedly, "this conversation included content from your mail" | The run is tainted: it read untrusted content, and a write tool (memory, tasks) was escalated. | Expected. Confirm, or ask again in a new conversation without reading the content first. |
| "Confirmed, but not today" does not confirm | Only an exact whole-utterance keyword executes without a model call; anything longer goes to the judge. | Say only the keyword, or answer plainly yes/no. |
| Confirmation does nothing after a pause | The pending action expired (`confirmation.ttl`, 60 s). | Ask again. |
| `The approval expired; the action was not executed.` | A background approval passed `tasks.approvalTtl`. | Decide faster or raise the TTL; see [approvals](tasks/approvals.md). |
| `Background tasks are stopped (kill switch).` | The kill switch is active. | Press **Resume** on the Tasks page. |
| `Today's cost cap for background tasks is reached.` | `tasks.dailyCostCap` used up. | Wait for the next local day or raise the cap, see [`tasks`](../configuration/marshal-yaml/tasks.md). |
| `The task's budget (or its parent's) is used up.` | The task is `failed` with `budget_exceeded`. | Create the task again with a narrower goal, or raise `tasks.defaultBudget`. |
| `A schedule may run at most every 5 minutes.` | Cron faster than `tasks.minWakeupInterval`. | Use a slower schedule. |
| `'<x>' is not a five-field cron expression.` | Invalid cron. | Use `minute hour day month weekday`, for example `0 6 * * *`. |
| `Low-priority deliveries never use voice; use inbox or push.` and other delivery errors | Invalid `deliver` entry. | See [scheduled tasks](tasks/scheduled-tasks.md#steps), step "Choose deliveries". |
| `A task named '<x>' already exists.` | Names are unique per owner. | Use `tasks.update` or another name. |
| A watch never fires | The filter does not match: wrong `source`, `type`, `entity` glob or payload field; or it is a `once` watch that already ran. | Check the event in [events](../reference/events.md); `tasks_list` shows `once`. Untrusted `ha` events still match, they only taint the run. |
| The briefing is not spoken | The `when` conditions never hold before `expires`; it was pushed instead. | Check the sensors, see [daily briefing](tasks/daily-briefing.md). |
| Task run in a satellite's context lacks `mail_*` tools | The origin satellite denies them (`denyTools`), and the policy applies to all its runs and child tasks. | Create the task from another device or change the [satellite profile](../configuration/satellites-yaml.md). |

## Memory

| Symptom | Cause | Fix |
|---|---|---|
| Memory misses a fact in another language | The embedding model is not multilingual. | Use a multilingual model; Marshal re-embeds at the next start. |
| `/health` memory check unhealthy | The `sqlite-vec` extension did not load. | Check the image platform and `/health` text; use the official image. |
| Wrong project active | Inferred activation. | Say "not that project" ( `memory.correct_project`) or fix aliases in **Memory**, Projects. |
| A remembered fact is gone | Soft-deleted or superseded. | Open the chunk history in **Memory**. |

## Backups

| Symptom | Cause | Fix |
|---|---|---|
| `No Data Protection keys at /data/keys to back up` | No UI-managed secret was ever stored, so no keys exist. | None needed. |
| Secrets stored in the UI unreadable after restore | Restored the database without `keys/`. | Restore both together, see [backups and restore](backups-and-restore.md). |

## Related

- [Observability](observability.md), [configuration index](../configuration/index.md)
- [FAQ](../faq.md), [Home Assistant component troubleshooting](../home-assistant-component/troubleshooting.md)
