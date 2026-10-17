# Proposal

## Why

Home Assistant automations can ask an LLM for one-off results through AI Task (`ai_task.generate_data`): describe a camera snapshot, word a notification, classify something into fields. HA's own providers (OpenAI, Gemini, Ollama) know nothing about the household. When Marshal provides the AI Task entity, an automation gets Marshal's memory, projects and, when a template allows it, Marshal's tools under Marshal's guard.

## What Changes

- The HA custom component registers an `ai_task` entity with `GENERATE_DATA` and `SUPPORT_ATTACHMENTS`. It forwards each task (name, instructions, optional structure as JSON Schema, optional image attachments) to Marshal and returns Marshal's data to HA.
- New endpoint `POST /api/v1/ai-tasks` (multipart: task JSON plus attachments) with a new scope `aitasks:write`. It runs one headless agent run and returns `data` (text, or an object that matches the structure) without streaming.
- New session and run source `automation`. Automation sessions are one-shot and are not listed in chat history; their runs appear in the runs view with source `automation` and the task name as trigger.
- AI task runs use the agent template `ai_task` (a built-in default without tools; `/config/templates/ai_task.md` overrides it, so the template's allowlist decides which tools exist). Memory retrieval and the pinned layer work as in any run.
- Attachments must be images. They require a model with the `vision` capability in the template's tier and taint the run (taint source `attachment`), because images can carry text.
- Nobody can confirm during an AI task, so confirm-tier calls and taint escalations go to the approval queue, as in background runs. The run continues and its data says that the action is waiting for approval.
- Structured output: when a structure is given, the final reply is shaped into JSON that matches the schema and is validated. One repair attempt, then an error.

## Capabilities

### New Capabilities

- `ai-tasks`: the AI task endpoint, run behavior (template, model choice, attachments, taint, approval queue, structured output, limits) and errors.

### Modified Capabilities

- `ha-custom-component`: new requirement for the AI Task entity.
- `sessions-identity`: session source gains `automation`.
- `chat-history`: automation sessions are not listed.
- `agent-templates`: built-in `ai_task` template next to `default`.
- `approval-queue`: approvals for non-interactive runs without a task (executed on approval, result notified, no task to resume).

## Non-goals

- `generate_image`: Marshal has no image provider.
- Continuing an AI task as a conversation, or streaming its result.
- Attachments other than images (PDF, audio, video).
- Storing attachments as artifacts. The run records only their media type, size and hash.
- Sending HA state with the task. Instructions are what the automation writes; templated state inside them is the automation author's choice.
- Confirming actions inside an AI task.

## Impact

- `ha/custom_components/marshal`: new `ai_task` platform, client method, translations, tests; manifest gains `ai_task` as a dependency; minimum HA version with AI Task attachments.
- `src/Marshal.Api`: AI task endpoint, multipart parsing, `aitasks:write` scope in `ApiScopes`, OpenAPI v1 document.
- `src/Marshal.Core`: headless run for AI tasks (template, vision requirement, structured output shaping), built-in `ai_task` template.
- `src/Marshal.Abstractions`: `SessionSource.Automation`; taint source kind `attachment`.
- `src/Marshal.Data`: enum value only (stored as snake_case text, no migration needed for the column; verified in tasks).
- Web UI: runs view source filter gains `automation`.
- Docs: HA integration guide (AI Task usage, token scopes), configuration reference (`aiTasks.*`), template reference.
