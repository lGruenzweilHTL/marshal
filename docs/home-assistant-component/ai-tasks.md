# Run AI tasks from automations

Use Marshal as the AI Task provider in Home Assistant (HA), so an automation can ask Marshal a question, optionally with camera snapshots, and use the answer as text or structured data.

## Before you start

- The Marshal component is installed and connected. See [installation](installation.md).
- An API token with the scopes `conversation:write` and `aitasks:write`. A token created before AI tasks existed has only `conversation:write`; tokens keep their scopes, so create a new one.
- For tasks with images: a model with `vision: true` in the tier of the `ai_task` template (`fast` by default). See [models](../configuration/marshal-yaml/models.md).

## Steps

### 1. Create a token with both scopes

```sh
docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write --scope aitasks:write
```

Copy the token. It is shown once.

### 2. Enter the token in HA

If the component already runs with an older token, the first AI task fails with "The Marshal API token lacks the scope aitasks:write" and HA starts a reauthentication: open **Settings > Devices & services**, choose **Reconfigure** on the Marshal card that asks for authentication, and enter the new token. The flow checks that the token carries `aitasks:write`; the entry keeps its URL and conversations keep working throughout.

After the new token works, revoke the old one with `marshal tokens revoke <token-id>`.

### 3. Use the entity in an automation

The component adds the entity `ai_task.marshal`. Call the action **AI Task: Generate data**:

```yaml
alias: Describe the doorbell visitor
triggers:
  - trigger: state
    entity_id: binary_sensor.doorbell
    to: "on"
actions:
  - action: ai_task.generate_data
    data:
      entity_id: ai_task.marshal
      task_name: doorbell
      instructions: Who is at the door? Is there a parcel?
      structure:
        visitor:
          description: Who is at the door, in a few words
          required: true
          selector:
            text:
        parcel:
          description: Whether a parcel is visible
          required: true
          selector:
            boolean:
      attachments:
        - media_content_id: media-source://camera/camera.front_door
          media_content_type: image/jpeg
    response_variable: result
  - action: notify.mobile_app_phone
    data:
      message: "{{ result.data.visitor }}{{ ' with a parcel' if result.data.parcel else '' }}"
```

Without `structure`, `result.data` is the answer as text.

## What Marshal does with a task

- Each task is one run in a new session with source `automation`, trigger `ai_task:<task_name>`, the `ai_task` template and HA's configured language. The runs view lists it under **Automation**; the chat history does not.
- Memory works as in a conversation: Marshal can use what it knows about your home and you.
- The built-in `ai_task` template allows no tools. To let tasks read your calendar or printer, copy `deploy/config.example/templates/ai_task.md` to `/config/templates/ai_task.md` and edit its tool list. See [agent templates](../configuration/templates-format.md#built-in-templates).
- Nobody can confirm anything during a task. A tool call that needs confirmation goes to the approval queue and your phone; the task answers that the action waits. It runs only if you approve it later, and you get a low-priority notification with the result.
- Images are untrusted content. A run with images cannot write memory or tasks without your approval.

> [!WARNING]
> Marshal treats `instructions` as your own words. Do not template text from third parties (a to-do item someone else can edit, a message, a web page) into `instructions`. Pass such content as an attachment, or keep the template's tool list read-only.

## Costs and limits

- Every task is a model call, and a task with `structure` makes one or two more. A trigger that fires often (every motion event) costs accordingly. Check the volume in the runs view with the **Automation** filter.
- AI tasks count against `tasks.dailyCostCap`. When the cap is used up, tasks fail with "Marshal's daily cost cap is reached." until the next local day.
- A task runs for at most `aiTasks.timeoutSeconds` (60 s by default). Local vision models can be slow; prefer a fast vision model in the template's tier. See [`aiTasks`](../configuration/marshal-yaml/ai-tasks.md).

## Troubleshooting

| HA error | Cause |
|---|---|
| The Marshal API token lacks the scope aitasks:write | Create a token with both scopes and enter it in the reauthentication step. |
| Marshal has no model that can look at images for this task | No `vision: true` model in the template's tier. |
| Marshal's answer did not match the requested structure | The model could not produce matching JSON twice in a row. Simplify the structure or use a stronger tier in `ai_task.md`. |
| Marshal did not finish the task in time | The run took longer than `aiTasks.timeoutSeconds`. |
| Marshal does not support AI tasks; update Marshal | Marshal answered `404`: it predates AI tasks. |

## Related

- [AI tasks API](../api/ai-tasks.md)
- [Configure the component](configuration.md)
- [Approval queue and risk tiers](../concepts/risk-and-confirmation.md)
