# `aiTasks`

Limits for AI tasks, the runs Home Assistant automations start through the Marshal AI Task entity (`POST /api/v1/ai-tasks`). Changes need a restart.

## Example

```yaml
aiTasks:
  timeoutSeconds: 60
  maxAttachments: 4
  maxAttachmentBytes: 10485760
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `aiTasks.timeoutSeconds` | integer | `60` | A task still running after this many seconds is cancelled and answered with `504` (`ai_task.timeout`). Range 1 to 3600. The Home Assistant component waits 75 seconds, so keep this below that. |
| `aiTasks.maxAttachments` | integer | `4` | Most image attachments per task. More are answered with `413`. Range 1 to 32. |
| `aiTasks.maxAttachmentBytes` | integer | `10485760` (10 MB) | Largest size of one attachment in bytes. A larger one is answered with `413`. At most `26214400` (25 MB). |

The request body limit is `maxAttachments` × `maxAttachmentBytes` plus 64 KB for the task part.

AI tasks also count against [`tasks.dailyCostCap`](tasks.md): when the cap is used up, a task is answered with `429` (`ai_task.daily_cap`). The model tier and the tools come from the [`ai_task` template](../templates-format.md#built-in-templates); a task with images needs a model with `vision: true` in that tier ([models](models.md)).

## Validation

- `aiTasks.timeoutSeconds must be between 1 and 3600.`
- `aiTasks.maxAttachments must be between 1 and 32.`
- `aiTasks.maxAttachmentBytes must be between 1 and 26214400.`

## Related

- [AI tasks from Home Assistant](../../home-assistant-component/ai-tasks.md)
- [AI tasks API](../../api/ai-tasks.md)
- [Agent templates](../templates-format.md)
