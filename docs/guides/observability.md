---
icon: fa-solid fa-chart-line
---

# Observability

See what Marshal did and what it cost: runs, cost, health, the audit trail, and OpenTelemetry export.

## Before you start

- Signed in to the web UI. For OpenTelemetry, an OTLP gRPC collector (for example the OpenTelemetry Collector or Grafana Alloy).
- Metric and trace names: [metrics and traces](../reference/metrics-and-traces.md). Health: [health endpoints](../reference/health-endpoints.md).

## Run insights in the web UI

Every conversation turn and every headless task run is recorded as a run.

| Page | Shows |
|---|---|
| **Home** | Widgets for recent runs, today's cost and system health. A health warning moves the health widget up. |
| **Runs** | All runs (voice, web, task, api, automation) with filters by source, outcome and cost, and the time to first delta ("first words"), sortable slowest first. |
| **Run detail** | The context build (including active projects and the loaded tool namespaces, each with why), a timeline for voice runs (context build, model calls, tool calls, first words, end), each model call (model, tier, tokens, latency, time to first token, cost, reasoning collapsed or a note that it was not kept), each tool call (arguments, truncated or redacted result, risk tier, guard decision, confirmation reasons, taint sources) and the reply. |
| **Cost** | Cost per turn, per day and per tier, with the advisor on its own line. These are the numbers the budgets use. |
| **System** | Health details, app and supported API versions, the kill switch, and a link to the configuration view for admins. |

Outcomes are `ok`, `error`, `backgrounded` and `cancelled`. A tainted run is flagged, with the first tainting step. A guard decision of `escalated_by_taint` or `denied` explains why an action needed confirmation or did not happen.

## Run recording and redaction

All recording goes through one recorder that writes the database tables and emits OpenTelemetry from the same data. Step payloads (prompts, tool arguments and results, replies) are kept for `maintenance.payloadRetention` (30 days); metadata stays for `maintenance.metadataRetention` (365 days). See [backups and restore](backups-and-restore.md) for the purge job.

Results of tools flagged sensitive (for example mail reads) are stored as a reference (message id, path), not content. Adjust in [`recording`](../configuration/marshal-yaml/recording.md):

```yaml
recording:
  redactTools: ["notes.*"]      # always store as reference only
  keepContentTools: []          # store in full even if flagged sensitive
```

Model-call steps keep the model's reasoning. Once a redacted tool result is part of a run, the reasoning of later model calls in that run is not stored, because it may quote the redacted content; the step only records that reasoning was withheld.

Longer contents are truncated at 8000 characters in step payloads. Logs never contain secrets, message bodies, note contents, reasoning or full prompts.

## Audit log

Actions outside a run are written to the audit log with actor kind (`user`, `api_token`, `system`), actor id, action, target and details: UI actions, approvals, token and secret changes, the kill switch, CLI commands (for example `token.create` with actor kind `system`) and memory edits such as `memory.chunk.pin`.

## Export to OpenTelemetry

### Set the endpoint

```yaml
telemetry:
  otlpEndpoint: "http://otel-collector:4317"
```

Marshal exports over OTLP gRPC. Leave the key unset to export nothing. The same value can be set as `MARSHAL__Telemetry__OtlpEndpoint`. A restart applies the change.

### What you get

Traces follow the OpenTelemetry GenAI semantic conventions, version 1.41 (the version `Microsoft.Extensions.AI` implements). The resource attribute `marshal.genai_semconv_version` names it. Spans exist while the run executes:

- `invoke_agent <template>`: one per run, from the source `Marshal.Runs`.
- `chat` and `embeddings`: one per model or embedding call, from `Experimental.Microsoft.Extensions.AI`, with provider, model and token usage.
- `execute_tool <tool>`: one per tool call, with the risk tier and guard decision.
- MCP client spans from `Experimental.ModelContextProtocol`. MCP requests carry the trace context, so an MCP server that traces joins the run's trace.

Backends with GenAI views (Langfuse, Arize Phoenix, Grafana) show the runs without custom mapping. Marshal's own metrics come from the meter `Marshal`; the standard `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` come from the M.E.AI meter:

| Metric | Type | Attributes |
|---|---|---|
| `marshal.runs` | counter | `outcome` |
| `marshal.model.tokens` | counter | `direction` (`input`, `output`) |
| `marshal.tool.calls` | counter | `decision` (guard decision) |
| `marshal.run.cost` | histogram | none |

All names and attributes: [metrics and traces](../reference/metrics-and-traces.md).

### Send traces to Langfuse or Phoenix

Marshal exports OTLP gRPC only. Langfuse accepts only OTLP over HTTP, so put an OpenTelemetry Collector in between. This collector config sends traces to Langfuse Cloud (EU) and to a self-hosted Phoenix:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317

exporters:
  otlphttp/langfuse:
    endpoint: https://cloud.langfuse.com/api/public/otel
    headers:
      # base64 of "<public key>:<secret key>"
      Authorization: "Basic ${env:LANGFUSE_AUTH}"
  otlp/phoenix:
    endpoint: phoenix:4317
    tls:
      insecure: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [otlphttp/langfuse, otlp/phoenix]
```

Then set `telemetry.otlpEndpoint: "http://otel-collector:4317"`. Use one exporter if you run only one backend. Phoenix serves OTLP gRPC on port 4317 and its UI on port 6006.

### Content capture (off by default)

Without content capture, spans carry ids and metadata only. To see inputs and outputs in the backend, turn it on:

```yaml
telemetry:
  captureContent: true
```

> **Warning:** With content capture on, your telemetry backend stores personal data: what you said, tool arguments, tool results and replies. Marshal logs a warning at startup while it is on.

Spans then carry exactly what run recording stores: the user input and the reply on `invoke_agent`, and the arguments and result on `execute_tool`. Contents are truncated, results of sensitive tools appear only as their reference, and withheld reasoning stays withheld. System prompts and the full messages sent to the model never appear on any span.

### Alert on something useful

Examples: a rise in `marshal.runs{outcome="error"}`, many `marshal.tool.calls{decision="denied"}` after a config change, or `marshal.run.cost` above your daily cap.

## Logs and health

Set the log level with `logging.logLevel.default` (a Debug or Trace level also prints stack traces for configuration errors at startup). `GET /health` is unauthenticated, returns no values, and reports the checks `database`, `config`, `secrets`, `mcp`, `memory` (sqlite-vec) and `homeassistant`. `unhealthy` returns HTTP 503, `degraded` (for example an optional secret that does not resolve) returns 200 with warnings.

```sh
curl -s http://<marshal>:8080/health
```

## Check that it works

Send a chat message, open **Runs** and the new run. With OTLP configured, find the trace `invoke_agent default` in your backend.

## Related

- [Troubleshooting](troubleshooting.md), [metrics and traces](../reference/metrics-and-traces.md), [`recording`](../configuration/marshal-yaml/recording.md)
- [Privacy](../concepts/privacy.md)
