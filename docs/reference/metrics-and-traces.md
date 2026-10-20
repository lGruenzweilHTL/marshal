# Metrics and traces

The OpenTelemetry instruments Marshal emits. Traces follow the OpenTelemetry GenAI semantic conventions, version 1.41. Marshal's own metrics come from the data the run recorder stores, so they always match the run records.

## Export

Set `telemetry.otlpEndpoint` in `marshal.yaml` to export over OTLP gRPC, for example `http://otel-collector:4317`. Without it, Marshal emits the instruments but exports nothing. See [`instance`, `paths`, `logging`, `telemetry`](../configuration/marshal-yaml/instance.md).

| Name | Kind |
|---|---|
| `Marshal.Runs` | Activity source: `invoke_agent` and `execute_tool` spans |
| `Experimental.Microsoft.Extensions.AI` | Activity source and meter: `chat` and `embeddings` spans, `gen_ai.client.*` metrics |
| `Experimental.ModelContextProtocol` | Activity source: MCP client spans |
| `Marshal` | Meter: Marshal's run metrics |

The resource attribute `marshal.genai_semconv_version` names the convention version (`1.41`). The conventions are still in Development status, so a later version may rename attributes; Marshal updates the version with the `Microsoft.Extensions.AI` package.

Without `telemetry.captureContent`, traces and metrics carry ids and metadata only, never message content, tool arguments or results. See [content capture](#content-capture).

## Metrics

The meter `Marshal` has four instruments.

| Instrument | Type | Unit | Tags | Description |
|---|---|---|---|---|
| `marshal.runs` | counter (integer) | none | `outcome` | Completed runs by outcome. |
| `marshal.model.tokens` | counter (integer) | `{token}` | `direction` | Model tokens by direction. Counts every recorded step with token data. |
| `marshal.tool.calls` | counter (integer) | none | `decision` | Tool calls by guard decision. |
| `marshal.run.cost` | histogram (double) | none | none | Cost per run, computed from the `price` settings of the models used. |

Tag values:

| Tag | Values |
|---|---|
| `outcome` | `ok`, `error`, `backgrounded`, `cancelled` |
| `direction` | `input`, `output` |
| `decision` | `safe`, `pending`, `confirmed_by_keyword`, `judge_positive`, `judge_neutral`, `judge_negative`, `escalated_by_taint`, `denied`, `expired`, `confirmed_in_ui` (see [risk tiers](risk-tiers.md#guard-decisions)) |

Marshal records `marshal.runs` and `marshal.run.cost` when a run completes. It records `marshal.model.tokens` and `marshal.tool.calls` as each step is recorded.

The meter `Experimental.Microsoft.Extensions.AI` adds the convention metrics `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` per model and embedding call. Token usage therefore appears twice: `marshal.model.tokens` matches the runs view, `gen_ai.client.token.usage` is the standard metric for GenAI dashboards.

## Traces

Each run is one trace. The spans exist while the run executes, so model calls and MCP requests parent on them.

```
invoke_agent default                 Marshal.Runs
├── chat qwen3                       Experimental.Microsoft.Extensions.AI
├── execute_tool ha_HassTurnOn       Marshal.Runs
│   └── tools/call                   Experimental.ModelContextProtocol
└── chat qwen3
```

### Spans

| Span | Source | Description |
|---|---|---|
| `invoke_agent <template>` | `Marshal.Runs` | The whole run. A subagent run is a child `invoke_agent` span. |
| `chat <model>` | `Experimental.Microsoft.Extensions.AI` | A model call, including structured output and summarizer calls of the run. |
| `embeddings <model>` | `Experimental.Microsoft.Extensions.AI` | An embedding call. |
| `execute_tool <tool>` | `Marshal.Runs` | A tool call, from the guard decision to the result. A frozen action executed after confirmation is an `execute_tool` span of the confirming run. |
| MCP client span | `Experimental.ModelContextProtocol` | A request to an MCP server. Its trace context goes to the server in `_meta` (`traceparent`, `tracestate`). |

### Attributes on `invoke_agent`

| Attribute | Description |
|---|---|
| `gen_ai.operation.name` | `invoke_agent`. |
| `gen_ai.agent.name` | The agent template, `default` for conversation turns. |
| `gen_ai.conversation.id` | The session id, when the run belongs to a session. |
| `marshal.run_id` | The run id. Matches the run in the web UI. |
| `marshal.source` | `voice`, `web`, `task`, `api` or `automation`. |
| `marshal.input_mode` | `text` or `voice`. |
| `marshal.trigger` | What started the run, for example `turn`, `task`, `continuation`, `confirmation` or `delegate`. |
| `marshal.outcome` | `ok`, `error`, `backgrounded` or `cancelled`. An `error` outcome also sets the span status to error. |
| `marshal.outcome_reason` | The reason for the outcome, when there is one. |
| `marshal.tainted` | `true` when the run read untrusted content. |

### Attributes on `execute_tool`

| Attribute | Description |
|---|---|
| `gen_ai.operation.name` | `execute_tool`. |
| `gen_ai.tool.name` | The tool name the model sees, for example `ha_HassTurnOn`. |
| `gen_ai.tool.call.id` | The model's call id. |
| `gen_ai.tool.type` | `function`. |
| `marshal.tool` | The internal tool id (see [tools](tools.md)). |
| `marshal.risk_tier` | `safe`, `confirm` or `forbidden`. |
| `marshal.guard_decision` | The guard decision. |
| `error.type` | The error code when the tool failed. The span status is then error. A guard denial is not an error. |

The `chat` and `embeddings` spans carry the convention attributes of `Microsoft.Extensions.AI`, among them `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens`. Marshal turns their sensitive data mode off, so they never carry messages.

Attributes without a value are omitted.

### Content capture

With `telemetry.captureContent: true`, Marshal adds the content that run recording stores. Contents are truncated like step payloads, sensitive results appear as their reference, and withheld reasoning is left out.

| Attribute | Span | Content |
|---|---|---|
| `marshal.input` | `invoke_agent` | The user input of the turn. |
| `marshal.reply` | `invoke_agent` | The reply. |
| `gen_ai.tool.call.arguments` | `execute_tool` | The tool arguments as JSON. |
| `gen_ai.tool.call.result` | `execute_tool` | The result, or the reference for a sensitive tool. |

No span carries the system prompt or the full messages sent to the model.

## Example queries

| Question | Query idea |
|---|---|
| Are runs failing? | Rate of `marshal.runs{outcome="error"}`. |
| Did a config change block tools? | Rate of `marshal.tool.calls{decision="denied"}`. |
| How much do I spend per run? | Percentiles of `marshal.run.cost`. |
| Which tool is slow? | Duration of `execute_tool` spans grouped by `marshal.tool`. |
| Which model is slow? | `gen_ai.client.operation.duration` grouped by `gen_ai.request.model`. |

## Related

- [Observability guide](../guides/observability.md)
- [Health endpoints](health-endpoints.md)
- [Risk tiers](risk-tiers.md)
