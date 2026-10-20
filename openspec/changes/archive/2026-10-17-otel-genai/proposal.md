# Proposal

## Why

Marshal exports one trace per run, but with its own `marshal.*` attribute names, built after the run ends. Tools such as Langfuse, Arize Phoenix or Grafana's GenAI views understand the OpenTelemetry GenAI semantic conventions (`invoke_agent`, `chat`, `execute_tool`, `gen_ai.*` attributes), not Marshal's names. Because spans are built after the fact, calls to MCP servers cannot carry the trace context, so a server's own spans never join Marshal's trace.

## What Changes

- Runs are traced live: an `invoke_agent` span for the run, a `chat` span per model call and an `execute_tool` span per tool call, all with the GenAI semantic convention attributes (operation, provider, request and response model, token usage, agent and conversation ids, tool name and call id) next to the existing `marshal.*` attributes (run id, source, guard decision, taint).
- Model calls are traced through `Microsoft.Extensions.AI`'s OpenTelemetry chat client and embedding generator, which implement the conventions and the standard `gen_ai.client.*` metrics.
- The conventions are still in Development status. Marshal pins the convention version that its `Microsoft.Extensions.AI` version implements and states it in a span attribute and the docs.
- Content capture is off by default. With `telemetry.captureContent: true`, spans additionally carry the user input, tool arguments, tool results and the reply, using exactly the redaction of run recording (sensitive results only as references, withheld reasoning stays withheld, never full prompts or system prompts).
- MCP calls carry the W3C trace context (`traceparent`, `tracestate`) in `_meta`, as the 2026-07-28 MCP specification describes, so servers that trace join the run's trace.
- Marshal's own metrics (`marshal.runs`, `marshal.model.tokens`, `marshal.tool.calls`, `marshal.run.cost`) stay.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `run-recording`: traces follow the GenAI semantic conventions, are emitted live, and content capture is opt-in with the recording redaction.
- `mcp-client`: trace context propagation to MCP servers.

## Non-goals

- OTLP logs export or log correlation changes.
- Sending full prompts, system prompts or unredacted tool results to a telemetry backend, even with content capture on.
- A built-in trace viewer; the runs view stays Marshal's own view.
- Tracing inside the executor host.

## Impact

- `src/Marshal.Core`: run root and tool spans in the agent runner; `OpenTelemetryRunObserver` keeps metrics only.
- `src/Marshal.Api`: `UseOpenTelemetry()` on chat clients and embedding generators per provider; tracing sources registered; `telemetry.captureContent` option.
- `src/Marshal.Core/Mcp`: trace context in `_meta` (or verification that the SDK does it).
- Docs: observability guide (backends, convention version, content capture warning).
