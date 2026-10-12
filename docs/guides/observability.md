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
| **Runs** | All runs (voice, web, task, api) with filters by source, outcome and cost. |
| **Run detail** | The context build (including active projects and why), each model call (model, tier, tokens, latency, time to first token, cost), each tool call (arguments, truncated or redacted result, risk tier, guard decision, confirmation reasons, taint sources) and the reply. |
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

Longer contents are truncated at 8000 characters in step payloads. Logs never contain secrets, message bodies, note contents or full prompts.

## Audit log

Actions outside a run are written to the audit log with actor kind (`user`, `api_token`, `system`), actor id, action, target and details: UI actions, approvals, token and secret changes, the kill switch, CLI commands (for example `token.create` with actor kind `system`) and memory edits such as `memory.chunk.pin`.

## Export to OpenTelemetry

### 1. Set the endpoint

```yaml
telemetry:
  otlpEndpoint: "http://otel-collector:4317"
```

Marshal exports over OTLP gRPC. Leave the key unset to export nothing. The same value can be set as `MARSHAL__Telemetry__OtlpEndpoint`. A restart applies the change.

### 2. What you get

- Traces from the activity source `Marshal.Runs`: one trace per run named `marshal.run`, with one child span per step (`marshal.context`, `marshal.model_call`, `marshal.tool_call`, `marshal.reply`). Spans carry ids and metadata only (run id, source, input mode, trigger, outcome, tainted flag, model, tier, tool, guard decision, token counts), never content.
- Metrics from the meter `Marshal`:

| Metric | Type | Attributes |
|---|---|---|
| `marshal.runs` | counter | `outcome` |
| `marshal.model.tokens` | counter | `direction` (`input`, `output`) |
| `marshal.tool.calls` | counter | `decision` (guard decision) |
| `marshal.run.cost` | histogram | none |

### 3. Alert on something useful

Examples: a rise in `marshal.runs{outcome="error"}`, many `marshal.tool.calls{decision="denied"}` after a config change, or `marshal.run.cost` above your daily cap.

## Logs and health

Set the log level with `logging.logLevel.default` (a Debug or Trace level also prints stack traces for configuration errors at startup). `GET /health` is unauthenticated, returns no values, and reports the checks `database`, `config`, `secrets`, `mcp`, `memory` (sqlite-vec) and `homeassistant`. `unhealthy` returns HTTP 503, `degraded` (for example an optional secret that does not resolve) returns 200 with warnings.

```sh
curl -s http://<marshal>:8080/health
```

## Check that it works

Send a chat message, open **Runs** and the new run. With OTLP configured, find the trace `marshal.run` in your backend.

## Related

- [Troubleshooting](troubleshooting.md), [metrics and traces](../reference/metrics-and-traces.md), [`recording`](../configuration/marshal-yaml/recording.md)
- [Privacy](../concepts/privacy.md)
