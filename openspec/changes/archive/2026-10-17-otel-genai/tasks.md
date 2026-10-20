# Tasks

## 1. Live run and tool spans

- [x] 1.1 Write failing tests with an `ActivityListener`: a run produces an `invoke_agent` span that is `Activity.Current` during model and tool calls; each tool call produces a child `execute_tool` span with tool name, call id, risk tier and guard decision; Marshal attributes (run id, source, input mode, trigger, outcome, taint) on the root; no content attributes. Verify they fail.
- [x] 1.2 Implement the spans in the agent runner and remove trace building from `OpenTelemetryRunObserver` (metrics stay). Verify the 1.1 tests and the existing observer metric tests pass.
- [x] 1.3 Write a failing test that `GenAiConventions.Version` is set as resource attribute `marshal.genai_semconv_version`; confirm the version M.E.AI 10.10 implements and set the constant. Verify the test passes.

## 2. Model and embedding spans

- [x] 2.1 Write failing tests in `Marshal.Api.Tests` that every chat client and embedding generator built by the provider setup is wrapped with OpenTelemetry (a fake inner client; an `ActivityListener` sees a `chat` / `embeddings` span parented on the current activity) and that sensitive data is disabled. Verify they fail.
- [x] 2.2 Add `UseOpenTelemetry()` to the provider builders and register the M.E.AI source and meter in tracing and metrics. Verify the 2.1 tests pass.

## 3. Content capture

- [x] 3.1 Write failing tests: with `telemetry.captureContent: true`, the root carries user input and reply and tool spans carry arguments and results as recorded; a sensitive tool's span carries the reference; withheld reasoning never appears; no span carries the system prompt or full messages; startup logs a warning. Verify they fail.
- [x] 3.2 Implement the option, its validation and the attributes from the redacted step records. Verify the 3.1 tests pass.

## 4. MCP trace context

- [x] 4.1 Write a failing test with an in-process MCP server that reads `_meta.traceparent` of a `tools/call` made in a run and compares it with the `execute_tool` span. Verify it fails or passes as is, and record which in design.md decision 4.
- [x] 4.2 If needed, inject `traceparent` / `tracestate` in `McpToolSource`. Verify the 4.1 test passes.

## 5. Documentation

- [x] 5.1 Update the observability docs: span structure, convention version, metrics, `telemetry.captureContent` with a warning, a Langfuse/Phoenix setup example, and the old-to-new span name mapping in `CHANGELOG.md`. Verify the pages follow `docs/development/docs-style.md`.

## 6. Integration check

- [x] 6.1 Run `dotnet test` and `openspec validate otel-genai --strict`. Verify both pass.
