# Design

## Context

See proposal.md (Why). Facts that shape the approach:

- HA (since 2025.8) calls `AITaskEntity._async_generate_data(task: GenDataTask, chat_log: ChatLog) -> GenDataTaskResult`. `GenDataTask` has `name`, `instructions`, `structure` (HA schema built from selectors, or `None`), `attachments` (`conversation.Attachment` with `media_content_id`, `mime_type`, `path` to a resolved local file) and `llm_api`. The result is `GenDataTaskResult(conversation_id, data)`. Feature flags: `AITaskEntityFeature.GENERATE_DATA | SUPPORT_ATTACHMENTS`.
- Marshal already runs headless loops: `AgentRunRequest` with a `MarshalExecutionContext`. The runner suspends into the approval queue only when the run belongs to a task (`TaskId` set and `IsInteractive = false`, `AgentRunner.Suspends`); other runs keep a pending action in their session and wait for an in-conversation confirmation. AI tasks have no task and no user in the conversation, so they need a third path.
- `AgentRunRequest.UserText` is a string. The loop has no way to pass images yet; printer inspection calls a vision model directly, outside the loop.
- `EfTaskSpending` sums only runs with source `task` for the daily cost cap.
- `SessionSource` is stored as snake_case text, so a new enum value needs no column change.

## Goals / Non-Goals

**Goals:**
- One automation action gets Marshal's memory and, if the template allows, its tools, with the same guard as every other run.
- Safe defaults: the built-in template has no tools; images taint.

**Non-Goals:**
- A second, simpler model path that bypasses the agent loop. AI tasks are ordinary runs so that recording, cost, guard and memory stay uniform.

## Decisions

### 1. Separate endpoint and scope, not the turn endpoint

`POST /api/v1/ai-tasks` on the v1 route group, multipart, JSON response. The turn endpoint streams SSE, continues sessions and carries satellite fields; none of that applies. A separate scope `aitasks:write` lets the user give the HA token this power deliberately. The existing HA token keeps working for conversations; the AI Task entity reports a clear error until the token has the scope.

Alternative: reuse `conversation:write` — fewer steps for the user, but an existing token would silently gain a new capability.

### 2. Headless run with source `automation`

New `SessionSource.Automation`. `AiTaskRunner` (in `Marshal.Core`) builds the execution context (`Source = Automation`, `IsInteractive = false`, template `ai_task`, output mode text) and calls `IAgentRunner` with `Trigger = "ai_task:<name>"`. A new session per task; nothing continues it. `EfConversationReader` excludes `Automation` like `Task`. `EfTaskSpending` counts `Task` and `Automation` toward the daily cap.

New interfaces:
- `IAiTaskRunner` (`Marshal.Core`): `RunAsync(AiTaskRequest, CancellationToken) → Result<AiTaskResult>`. v1: `AiTaskRunner`.
- `IStructuredOutputShaper` (`Marshal.Core`): `ShapeAsync(modelRoute, instructions, reply, schema) → Result<JsonElement>`. v1: `ModelStructuredOutputShaper`, which asks the run's model with `ChatResponseFormat.ForJsonSchema` and validates the result.

### 3. Images through the loop

`AgentRunRequest` gains `UserAttachments` (list of `DataContent`). The context builder adds them to the first user message next to `UserText`. The router is asked for the template's tier plus `vision` when attachments exist; no match fails before any model call (`ai_task.no_vision_model`). The run's execution context gets taint source `attachment` before the first model call, so the guard sees the taint from step one.

Alternative: describe the image with a separate vision call and pass the description as text — works with non-vision main models, but adds a call and loses detail. Rejected for v1; a template can pick a vision tier.

### 4. Structured output as a shaping step after the loop

The loop runs without a response format: some providers reject tools together with a JSON schema format, and the model should be free to call tools first. After the final reply, `IStructuredOutputShaper` makes one call with the schema as response format and the instructions plus the reply as input, then validates against the JSON Schema. On failure, a second call includes the validation errors. The shaping calls are recorded as model-call steps of the same run.

Schema validation uses `System.Text.Json` with `JsonSchema` support in .NET 10 (`JsonSchemaExporter` exists, validation does not). Verify when building whether `Microsoft.Extensions.AI` offers validation. Otherwise implement a small validator for the subset HA produces (object, required, string, number, integer, boolean, enum, array of these). HA's selectors only produce that subset.

Finding (checked against `Microsoft.Extensions.AI.Abstractions` 10.10.1): the library has no instance validation. `AIJsonUtilities` creates schemas (`CreateJsonSchema`) and only checks that a schema document is a JSON object or boolean (`ValidateSchemaDocument`); `ChatResponse<T>` deserializes without checking the schema. Marshal therefore implements `JsonSchemaSubsetValidator` for the subset above, ignoring unknown keywords.

### 5. Approvals without a task

New path in the runner: when the context is non-interactive and has no `TaskId`, a pending action is written to the approval queue with `task_id = null` (the column becomes nullable) instead of to the session, and the loop continues with a fixed tool result ("waiting for approval in the review queue"). `ApprovalQueue` executes such actions on approval and sends a `low` notification with the result, because there is no task to resume. The MCP server change reuses this path for `ask_marshal`.

Built: `pending_actions.task_id` was already nullable (session pending actions have no task), so a null task cannot mark a queue entry. A new column `queued` marks every approval-queue entry, with or without a task; the migration sets it for existing rows with a task. Session lookups (`GetPendingAsync`, supersede, drop on close) skip queued rows, so input in the session never confirms them.

### 6. Tools and tiers

AI tasks add no model-facing tools. The visible tools are the `ai_task` template's allowlist, filtered as usual: with `IsInteractive = false`, confirm-tier tools stay visible and go to the approval queue (same as background tasks). The built-in template: `modelTier: fast`, `tools: []`, a short prompt ("Answer the instructions; do not ask questions; nobody can answer them").

### 7. Component side

`ai_task.py` platform with `MarshalAITaskEntity`. Structure conversion: HA's OpenAI integration converts `task.structure` with `voluptuous_openapi.convert`. Use the same helper so Marshal receives JSON Schema. Verify the helper name against the HA version pinned in `ha/` when building. Finding (HA 2026.9.4, pinned through `pytest-homeassistant-custom-component` 0.13.367): the OpenAI integration no longer uses `voluptuous_openapi`; it calls `probatio.to_openapi(schema, custom_serializer=llm.selector_serializer)` and then applies OpenAI-only strictness (`_adjust_schema`). `probatio` is a core requirement of HA, so the component calls `to_openapi` with `llm.selector_serializer` and sends the result without the OpenAI adjustments. Attachments are read from `attachment.path` in the executor (`hass.async_add_executor_job`) and sent as multipart parts with their `mime_type`. The client timeout is `aiTasks.timeoutSeconds` plus a margin, so Marshal's 504 arrives first. `language` = `hass.config.language`.

### 8. Limits

Config `aiTasks: { timeoutSeconds: 60, maxAttachments: 4, maxAttachmentBytes: 10485760 }`, validated at startup (positive values; bytes at most 25 MB). Request body limit = `maxAttachments × maxAttachmentBytes + 64 KB`.

## Risks / Trade-offs

- [Automation authors template untrusted HA state (e.g. a to-do item) into `instructions`] → Instructions are treated as trusted, like a typed turn. Document that templated third-party text belongs in an attachment or a separate field. The built-in template has no tools, so the default blast radius is a wrong text.
- [Vision models on local hardware are slow; HA automations wait] → The timeout returns 504 with a clear code. Documentation recommends a fast vision tier.
- [Automations fire often (every motion event) and cost money] → Runs count against the daily cost cap. The runs view shows source `automation` so the user can see the volume.
- [HA changes `GenDataTask` or the structure helper] → Component tests run against the pinned HA version; the manifest states the minimum version.

## Migration Plan

No data migration. Token scopes are fixed at creation, so the user creates a new token with `conversation:write` and `aitasks:write` and enters it through a new reauth step (`async_step_reauth` / `async_step_reauth_confirm`) that the component starts on a 403 from the AI task endpoint. Until then the entity exists and reports the scope error; conversations keep working with the old token. Rollback: older Marshal answers 404, which the component reports as "Marshal does not support AI tasks".
