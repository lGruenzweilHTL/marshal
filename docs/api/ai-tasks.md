---
icon: fa-solid fa-robot
order: 30
description: "POST /api/v1/ai-tasks runs one task for an automation and answers with JSON."
---

# AI tasks

`POST /api/v1/ai-tasks` runs one task for an automation and answers with JSON. The Home Assistant component's AI Task entity uses it; any client with the scope `aitasks:write` can.

An AI task is an ordinary agent run: same guard, memory, recording and cost accounting as a conversation turn. It runs in a new session with source `automation` and trigger `ai_task:<taskName>`, with the [`ai_task` template](../configuration/templates-format.md#built-in-templates). The session is closed after the task; nothing continues it, and it does not appear in the chat history. The endpoint does not stream.

## Request

`multipart/form-data` with these parts:

| Part | Count | Content |
|---|---|---|
| `task` | exactly one | JSON object, see below |
| `attachment` | 0 to `aiTasks.maxAttachments` | an image: `image/jpeg`, `image/png` or `image/webp`, at most `aiTasks.maxAttachmentBytes` |

The `task` part:

| Field | Type | Required | Description |
|---|---|---|---|
| `taskName` | string | yes | Name of the task. The run's trigger is `ai_task:<taskName>`. |
| `instructions` | string | yes | What to do. Treated as trusted input, like a typed turn. |
| `structure` | object | no | JSON Schema of the answer. Without it, `data` is a string. |
| `language` | string | no | Reply language, for example `de` or `en-GB`. German and English are supported; anything else falls back to your preferred language. |

```http
POST /api/v1/ai-tasks HTTP/1.1
Authorization: Bearer <token>
Content-Type: multipart/form-data; boundary=x

--x
Content-Disposition: form-data; name="task"
Content-Type: application/json

{"taskName":"doorbell","instructions":"Who is at the door? Is there a parcel?","language":"en",
 "structure":{"type":"object","properties":{"visitor":{"type":"string"},"parcel":{"type":"boolean"}},"required":["visitor","parcel"]}}
--x
Content-Disposition: form-data; name="attachment"; filename="door.jpg"
Content-Type: image/jpeg

<image bytes>
--x--
```

## Response

`200` with the answer and the run's id (open it in the runs view):

```json
{ "data": { "visitor": "a courier", "parcel": true }, "runId": "0199…" }
```

Without `structure`, `data` is the reply text as a JSON string.

### Structure

The loop runs without a response format, so the model may call tools first. After the final reply, Marshal makes one more model call that turns the reply into JSON with the schema as response format, and validates the result. On failure it tries once more with the validation errors. Both calls are recorded as model calls of the run.

Validation covers the subset Home Assistant produces: `type` (`object`, `string`, `number`, `integer`, `boolean`, `array`, `null`, or a list of these), `properties`, `required`, `items` and `enum`. Other keywords are ignored.

### Images

A task with attachments needs a model with `vision: true` in the template's tier. The images go to the model with the instructions. The run is tainted with taint source `attachment` from its first model call, so memory and task writes in that run need approval. The run record keeps each image's media type, size and SHA-256, never the image.

### Tools and approvals

The built-in `ai_task` template allows no tools. If you allow tools in `/config/templates/ai_task.md`, the run is non-interactive: a confirm-tier call, or a `persists` call escalated by taint, goes to the approval queue with a push notification instead of running. The run continues and its answer says that the action waits. When you approve it later, Marshal runs exactly the frozen call and sends you a `low` notification with the result. Rejection or expiry runs nothing.

## Errors

| Status | Code | Cause |
|---|---|---|
| `400` | `ai_task.invalid_request` | No `task` part, invalid JSON, empty `taskName` or `instructions`, or `structure` is not an object. |
| `403` | | The token lacks `aitasks:write`. |
| `413` | `ai_task.too_large` | Too many attachments, or one is larger than `aiTasks.maxAttachmentBytes`. |
| `415` | `ai_task.unsupported_attachment` | An attachment is not JPEG, PNG or WebP. |
| `422` | `ai_task.no_vision_model` | Attachments, but no vision model in the template's tier. No model call is made. |
| `429` | `ai_task.daily_cap` | `tasks.dailyCostCap` is reached, before or during the run. |
| `502` | `ai_task.invalid_output` | The answer did not match `structure` after the repair attempt. The run is recorded with outcome `error`. |
| `502` | `ai_task.failed` | The run ended otherwise, for example at the step limit. |
| `504` | `ai_task.timeout` | The run took longer than `aiTasks.timeoutSeconds` and was cancelled. |

## Related

::: cards
- [AI tasks from Home Assistant](../home-assistant-component/ai-tasks.md)
- [`aiTasks` configuration](../configuration/marshal-yaml/ai-tasks.md)
- [Authentication and scopes](authentication.md)
- [Untrusted content](../concepts/untrusted-content.md)
:::
