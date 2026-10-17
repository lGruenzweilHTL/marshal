# Tasks

## 1. Source, scope and configuration

- [x] 1.1 Write failing tests: `SessionSource.Automation` round-trips through the DB as `automation`; `EfConversationReader` hides automation sessions from list and detail; `EfTaskSpending` counts automation runs toward the daily cap. Verify they fail.
- [x] 1.2 Add `SessionSource.Automation`, extend the reader filter and the spending query. Verify the 1.1 tests pass.
- [x] 1.3 Write a failing test that `ApiScopes.All` contains `aitasks:write` and that the role-derived scopes of `admin` include it. Add the scope. Verify the test passes.
- [x] 1.4 Write failing options tests for `aiTasks` (defaults 60 s / 4 / 10 MB; zero or negative values and bytes above 25 MB fail startup naming the key). Implement `AiTaskOptions` with its validator. Verify the tests pass.
- [x] 1.5 Add the taint source kind `attachment` with its confirmation reason sentence in German and English; write a failing test on `ConfirmationReasonText` for it, then implement. Verify the test passes.

## 2. Built-in template and images in the loop

- [x] 2.1 Write failing template catalog tests: a built-in `ai_task` template (fast tier, no tools) exists; `/config/templates/ai_task.md` replaces it on hot reload. Implement. Verify they pass.
- [x] 2.2 Write failing agent runner tests with `FakeChatClient`: `UserAttachments` arrive as `DataContent` in the first user message; the router is asked for the template tier plus `vision`; a missing vision model fails with `ai_task.no_vision_model` before any model call; the attachment taint is set before the first model call. Verify they fail.
- [x] 2.3 Add `AgentRunRequest.UserAttachments` and the vision requirement in the runner. Verify the 2.2 tests pass.

## 3. Approvals without a task

- [x] 3.1 Write failing tests: a non-interactive run without `TaskId` puts a confirm-tier call and a taint-escalated `persists` call into the approval queue with `task_id = null`, holds no session pending action, and continues with the waiting result; approving executes the frozen action once and sends a `low` result notification; rejection and expiry execute nothing; a later "yes" in the session confirms nothing. Make `task_id` nullable (migration, `erd.puml`) and implement the path in `AgentRunner` and `ApprovalQueue`. Verify the tests pass and the guard branch-coverage check passes.

## 4. Structured output

- [x] 4.1 Check whether `Microsoft.Extensions.AI` 10.10 validates JSON against a JSON Schema; record the finding in design.md decision 4. Verify the note is in design.md.
- [x] 4.2 Write failing validator tests for the HA subset (object, required, string, number, integer, boolean, enum, arrays of these; unknown keywords ignored). Implement the validator (or wire the library one). Verify the tests pass.
- [x] 4.3 Write failing `ModelStructuredOutputShaper` tests with `FakeChatClient`: valid output on the first try; invalid then valid (second call carries the validation errors); invalid twice fails with `ai_task.invalid_output`; both calls recorded as model-call steps. Implement `IStructuredOutputShaper`. Verify the tests pass.

## 5. AI task runner and endpoint

- [x] 5.1 Write failing `AiTaskRunner` tests: new session with source `automation` per task; trigger `ai_task:<name>`; language from the request, else the owner's preference; text output mode; timeout cancels the run with `ai_task.timeout`; cost cap refusal; a confirm-tier call from the template's tools creates an approval-queue entry and the run still returns data; with `structure` the shaper runs after the loop. Verify they fail.
- [x] 5.2 Implement `IAiTaskRunner` / `AiTaskRunner`. Verify the 4.1 tests pass.
- [x] 5.3 Write failing API integration tests (`WebApplicationFactory`): 403 without `aitasks:write`; 400 for a missing task part, empty instructions, non-object structure; 415 for a non-image attachment; 413 above the size limit; 422 without a vision model; 429 at the cost cap; 504 on timeout; 502 on invalid output; 200 with string `data` and with object `data`; the run record keeps attachment media type, size and SHA-256 but not the bytes. Verify they fail.
- [x] 5.4 Implement `POST /api/v1/ai-tasks` on the v1 route group with multipart parsing and problem details. Verify the 4.3 tests pass and `/openapi/v1.json` contains the endpoint.
- [x] 5.5 Add `automation` to the runs view source filter in `web/` (filter options and i18n in German and English) with a failing component test first. Regenerate the API client (`npm run gen:api`). Verify `npm test` and `npm run build` pass.

## 6. HA component

- [x] 6.1 Write failing `ha/tests/test_ai_task.py` tests: the entity exists with generate-data and attachment features; a task with structure and one attachment sends one multipart request with the task JSON (name, instructions, JSON Schema, language) and the file; the response's data is returned with the chat log's conversation id; 403 raises the translated scope error and starts reauth; unreachable, 4xx, 5xx and timeout raise translated errors. Verify they fail (`uv run pytest` in `ha/`).
- [x] 6.2 Verify the structure conversion helper HA's OpenAI integration uses in the pinned HA version; record it in design.md decision 7. Verify the note is in design.md.
- [x] 6.3 Implement `ai_task.py`, the client method in `api.py`, the platform registration, `ai_task` in the manifest dependencies, and German and English strings. Verify the 5.1 tests pass.
- [x] 6.4 Write failing config flow tests for `async_step_reauth` / `async_step_reauth_confirm` (valid token updates the entry; invalid token shows the auth error). Implement. Verify they pass.

## 7. Documentation and examples

- [x] 7.1 Add `deploy/config.example/templates/ai_task.md` (read-only tools, e.g. `calendar.list_events`, `printer.status`) with comments on the approval queue. Verify the template loads in a config load test.
- [x] 7.2 Document AI tasks in the HA integration guide (automation example with a camera snapshot and structure, token scopes, reauth, cost notes), `aiTasks.*` in the configuration reference, and the built-in `ai_task` template in the template reference, following `docs/development/docs-style.md`. Verify the docs link check (if present) passes and the examples match the spec.
- [x] 7.3 Update the `CLAUDE.md` repository map entry for `ha/` (AI Task entity). Verify the entry reads correctly.

## 8. Integration check

- [x] 8.1 Run `dotnet test`, `uv run pytest` in `ha/`, `npm test` and `npm run build` in `web/`, and `openspec validate ha-ai-task --strict`. Verify all pass.
