# Tasks

## 1. Protocol

- [x] 1.1 Write failing tests with in-process MCP servers: a 2026-07-28 server and a 2025-06-18 server both connect and list tools; the client declares no sampling, roots or logging capability and declares elicitation (form, URL) and the tasks extension; a list result with `ttlMs` suppresses re-listing until it passes unless a change notification arrives (`FakeTimeProvider`). Verify they fail.
- [x] 1.2 Configure the client options and `ttlMs` handling in `McpToolSource`. Verify the 1.1 tests pass.

## 2. System health

- [x] 2.1 Write failing tests for `AggregatedSystemHealth`: existing health checks map to codes, components and German and English messages; no external error text appears; providers' warnings are included; changes are published on the `health` live topic. Verify they fail.
- [x] 2.2 Implement `ISystemHealth`, `IHealthWarningProvider` and the aggregation; add `GET /api/v1/system/health` (human principals and `admin` tokens) with failing API tests first. Verify the tests pass and the OpenAPI document has the endpoint.
- [x] 2.3 Write failing tests for `system.health`: hidden without warnings; returns warnings in the run's language; output is trusted. Add `system` to `ReservedNamespaces` and implement the tool. Verify the tests pass.

## 3. Tool pinning

- [x] 3.1 Write failing fingerprint tests (canonical JSON; key order and whitespace do not matter; each of description, input schema, output schema and annotations changes the hash). Implement. Verify the tests pass.
- [x] 3.2 Write failing EF tests for `mcp_tool_pins` (no owner column, readable without execution context). Add the table, the migration and `erd.puml`; re-render the SVG. Verify the tests pass.
- [x] 3.3 Write failing `McpToolSource` tests: first discovery pins all as approved; a later changed or new tool is pending, hidden by the visibility filter and denied by the guard while the others stay visible; a returning unchanged tool is approved; `pinTools: false` skips pinning; a pending tool raises `mcp.tool_pending` and notifies the owner once. Verify they fail.
- [x] 3.4 Implement `IMcpToolPins` / `EfMcpToolPins`, the pin state on tools, the visibility filter and guard rule, the `pinTools` config key and the notification. Verify the 3.3 tests pass.
- [x] 3.5 Write failing API tests for listing pending tools with previous and current definitions and for approve / reject (admin scope, audit entry, effect on the next turn's tools). Implement the endpoints. Verify the tests pass.
- [x] 3.6 Regenerate the web client; write failing component tests for the approval list and side-by-side diff in the config view; implement with German and English strings. Verify `npm test` passes.

## 4. Elicitation

- [x] 4.1 Check how the SDK 2.2 client surfaces `input_required` results (raw result vs. handler) with an in-process server; record the finding in design.md decision 3. Verify the note is in design.md.
- [x] 4.2 Write failing tests: a form request creates a pending input request with frozen call and request state; the model's tool result contains none of the request's content; a URL request never appears in the model context; a background run suspends; a push with a link and without actions is sent; expiry resumes the task with `rejected: expired`. Verify they fail.
- [x] 4.3 Add `pending_input_requests` to the migration, implement `IInputRequestQueue` / `EfInputRequestQueue` and the conversion in `McpToolSource`. Verify the 4.2 tests pass.
- [x] 4.4 Write failing tests for answering: schema validation of form answers; `done` for URL mode; one repeated call with `inputResponses` and `requestState`; a chained second request; result delivered as a background result in the web session or as the task's resume payload; only cookie principals may answer. Implement the endpoint and executor. Verify the tests pass.
- [x] 4.5 Write failing component tests for the input request form (generated from the schema: string, number, boolean, enum) and the URL link with "Done" in chat and the review queue; implement with German and English strings. Verify `npm test` passes.

## 5. Server tasks

- [x] 5.1 Write failing tests with an in-process server that returns a task handle: a waiting agent task is created with the origin as delivery target; the tool result says the work continues; polling respects the poll interval and backoff (`FakeTimeProvider`); completion, failure and cancellation publish `mcp.task_finished` and resume the task with the outcome; handles survive a restart; cancelling the Marshal task cancels the server task; the per-owner handle cap. Verify they fail.
- [x] 5.2 Add `mcp_task_handles` to the migration and implement `IMcpTaskTracker` / `PollingMcpTaskTracker`. Verify the 5.1 tests pass.

- [x] 5.3 Write failing tests for a server task in `input_required` (its `inputRequests` from `tasks/get`): each becomes a pending input request tied to the handle, and the answer is sent with `tasks/update` (`inputResponses`) instead of repeating the call. Implement. Verify the tests pass.

## 6. Banner and docs

- [x] 6.1 Write failing component tests for the health banner (severity color, count, first message, admin link, dismissal per warning-code set, live update); implement it in the shell with German and English strings; add it to the preview scenarios. Verify `npm test`, `npm run build` and the screenshot sweep pass.
- [x] 6.2 Add `pinTools` with a comment to `deploy/config.example/mcp/ha.yaml` and `system.health` to the example briefing template's allowlist and skill. Verify the example config loads in the config load test.
- [x] 6.3 Document pinning, elicitation, server tasks, the health banner and `system.health` in the MCP guide and the configuration reference. Verify the pages follow `docs/development/docs-style.md`.

## 7. Integration check

- [x] 7.1 Run `dotnet test`, `npm test`, `npm run build` and `openspec validate mcp-2026-and-pinning --strict`. Verify all pass.
