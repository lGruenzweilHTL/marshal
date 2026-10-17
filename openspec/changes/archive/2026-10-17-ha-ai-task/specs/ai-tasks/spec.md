# Spec Delta

## Purpose

Lets Home Assistant automations run one-off AI tasks on Marshal: instructions plus optional structure and image attachments in, generated data out, with Marshal's memory, template-scoped tools and guard.

## ADDED Requirements

### Requirement: AI task endpoint
`POST /api/v1/ai-tasks` SHALL accept a multipart request with a `task` part (JSON: `taskName`, `instructions`, optional `structure` as a JSON Schema object, optional `language`) and zero or more `attachment` parts. It SHALL require the scope `aitasks:write`. It SHALL run one headless agent run for the token's owner and SHALL answer with JSON `{ data, runId }`, where `data` is a string without `structure` and an object matching `structure` otherwise. The endpoint SHALL NOT stream.

#### Scenario: Text task
- **WHEN** a token with `aitasks:write` posts `taskName = "garage"` and `instructions = "Write a short reminder that the garage is open"`
- **THEN** the response is 200 with a string `data` and the run's id

#### Scenario: Missing scope
- **WHEN** a token with only `conversation:write` posts a task
- **THEN** the response is 403 and no run is started

#### Scenario: Invalid request
- **WHEN** the `task` part is missing, `instructions` is empty, or `structure` is not a JSON Schema object
- **THEN** the response is 400 with a problem detail naming the field and no run is started

### Requirement: AI task runs
Each AI task SHALL run in a new one-shot session with source `automation` and SHALL be recorded as a run with source `automation` and trigger `ai_task:<taskName>`. The run SHALL use the agent template `ai_task`, text output mode, and the reply language from `language` when given, otherwise the owner's preferred language. Memory retrieval and the pinned layer SHALL apply as in any run. The run SHALL be limited by `aiTasks.timeoutSeconds` (default 60) and by the template's step limit, and SHALL count against the global daily cost cap; a run that is refused because the cap is reached SHALL answer 429.

#### Scenario: Recorded as automation run
- **WHEN** an AI task named `doorbell` completes
- **THEN** the runs list shows a run with source `automation` and trigger `ai_task:doorbell`

#### Scenario: Timeout
- **WHEN** a run is still working after `aiTasks.timeoutSeconds`
- **THEN** the run is cancelled and the response is 504 with error code `ai_task.timeout`

#### Scenario: Cost cap reached
- **WHEN** the global daily cost cap is reached and a task arrives
- **THEN** the response is 429 and no model is called

### Requirement: Attachments
Attachments SHALL be images (`image/jpeg`, `image/png`, `image/webp`), at most `aiTasks.maxAttachments` (default 4) of at most `aiTasks.maxAttachmentBytes` (default 10 MB) each. A task with attachments SHALL require a model with the `vision` capability in the template's tier; without one it SHALL fail with 422 and error code `ai_task.no_vision_model` before any model call. Attachments SHALL taint the run with taint source `attachment`. The run record SHALL keep each attachment's media type, size and SHA-256, not its content.

#### Scenario: Camera snapshot
- **WHEN** a task carries one JPEG and the template's tier has a vision model
- **THEN** the model receives the image and the run is tainted with source `attachment`

#### Scenario: Unsupported attachment
- **WHEN** a task carries a PDF
- **THEN** the response is 415 and no run is started

#### Scenario: No vision model
- **WHEN** a task carries an image and the template's tier has no model with `vision`
- **THEN** the response is 422 with `ai_task.no_vision_model`

### Requirement: Actions in AI tasks wait for approval
AI task runs SHALL be treated like background runs for confirmation: a confirm-tier call, or a `persists` call escalated by taint, SHALL create a frozen pending action in the approval queue and notify the owner, and the run SHALL continue with a tool result saying the action waits for approval. Nothing in the task request SHALL count as a confirmation.

#### Scenario: Confirm-tier call
- **WHEN** the `ai_task` template allows `calendar.create_event` and the run calls it
- **THEN** a pending action appears in the approval queue, the event is not created yet, and the run still returns data

#### Scenario: Tainted memory write
- **WHEN** a task with an attachment calls `memory_remember`
- **THEN** the call becomes a pending action with reason `taint` in the approval queue

### Requirement: Structured output
When `structure` is given, the system SHALL return an object that validates against it. After the agent loop ends, the final reply SHALL be shaped into JSON that matches the schema and validated; on failure the system SHALL make one repair attempt with the validation errors, and on a second failure SHALL answer 502 with error code `ai_task.invalid_output`.

#### Scenario: Valid structure
- **WHEN** `structure` requires a boolean `carrier` and a string `summary`
- **THEN** `data` is an object with both fields of those types

#### Scenario: Repair fails
- **WHEN** the shaped output fails validation twice
- **THEN** the response is 502 with `ai_task.invalid_output` and the run is recorded with outcome `error`
