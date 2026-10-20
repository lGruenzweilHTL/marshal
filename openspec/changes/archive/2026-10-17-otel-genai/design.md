# Design

## Context

See proposal.md (Why). Current state:

- `OpenTelemetryRunObserver` (an `IRunObserver`) collects steps while a run runs and creates the root and step activities after `RunCompleted`, with backdated start times. Activities therefore never exist while calls are in flight, so nothing downstream can parent on them.
- Tracing registers only the `Marshal.Runs` source; OTLP export when `telemetry:otlpEndpoint` is set.
- `Microsoft.Extensions.AI` 10.10 ships `OpenTelemetryChatClient` / `OpenTelemetryEmbeddingGenerator` (`UseOpenTelemetry()`), which implement the GenAI conventions (v1.38 per the docs of earlier versions; confirm for 10.10) including `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` metrics. Their `EnableSensitiveData` captures full messages, including raw tool results.
- `ModelContextProtocol.Core` 2.2.0 implements MCP 2026-07-28, which documents `traceparent` / `tracestate` / `baggage` in `_meta`.

## Goals / Non-Goals

**Goals:**
- Standard tools read Marshal traces without custom mapping.
- MCP server spans join run traces.
- One redaction rule for the database and telemetry.

**Non-Goals:**
- Replacing the runs view or the domain tables with telemetry.

## Decisions

### 1. Live spans from the runner, model spans from Microsoft.Extensions.AI

The agent runner starts the `invoke_agent` activity at run start (source `Marshal.Runs`), so it is `Activity.Current` for the whole run, and wraps each tool execution in an `execute_tool` activity. Model and embedding calls get `chat` / `embeddings` spans from `UseOpenTelemetry()` on every chat client and embedding generator built in `Marshal.Api`, with `EnableSensitiveData = false` always. Those spans parent automatically on the current activity. The observer keeps the `marshal.*` metrics and no longer builds traces.

Alternative: keep building spans after the run with convention names. Simple, but propagation to MCP and HTTP calls stays impossible.

### 2. Content capture only through Marshal's spans

M.E.AI's sensitive data mode would export raw tool results (mail bodies) inside the chat messages, bypassing the recording redaction. So it stays off, and content capture adds attributes only on Marshal's own spans, from the same redacted step records the recorder writes (`gen_ai.tool.call.arguments`, `gen_ai.tool.call.result`, user input and reply as Marshal attributes when the convention has no matching one). New interface: none. The runner already holds the redacted step data when it ends a span.

### 3. Convention version

A constant `GenAiConventions.Version` (the version M.E.AI implements), set as `gen_ai.semconv.version`-style resource attribute `marshal.genai_semconv_version` and named in the docs. Updating M.E.AI updates the constant in the same change.

### 4. MCP propagation

Check first whether the SDK's client already injects `traceparent` into `_meta` from `Activity.Current`. If yes, a test proves it with an in-process server; if not, `McpToolSource` adds `_meta` entries through the SDK's request options using `DistributedContextPropagator.Current`.

Result (task 4.1): `ModelContextProtocol.Core` 2.2.0 injects `traceparent` and `tracestate` into `_meta` of every request, but only from its own client span (activity source `Experimental.ModelContextProtocol`), which exists only while that source is listened to. Without a listener the test failed (no `_meta.traceparent`). Marshal therefore registers the SDK's source in tracing instead of injecting by hand: every request (calls, listing, discovery) carries the context, and the parent of a call's `traceparent` is the SDK's `tools/call` client span, a child of the `execute_tool` span. `McpToolSource` needs no change (task 4.2).

### 5. Configuration

`telemetry: { otlpEndpoint, captureContent: false }`. The existing key stays; `captureContent` is new, validated as bool, warning logged at startup when true.

## Risks / Trade-offs

- [Convention names change (Development status)] → pinned version attribute; tests assert the names this version uses, so an M.E.AI update that renames them fails loudly.
- [Double token counting between `marshal.model.tokens` and `gen_ai.client.token.usage`] → both stay; documented as Marshal's run view metric vs. the standard metric.
- [Live activities when no listener is configured] → `ActivitySource.StartActivity` returns null without listeners; negligible cost.

## Migration Plan

None for data. Dashboards built on the old post-hoc span names (`marshal.run`, `marshal.model_call`) need the new names; the changelog lists the mapping.
