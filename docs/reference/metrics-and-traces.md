# Metrics and traces

The OpenTelemetry instruments Marshal emits: one activity source for traces and one meter for metrics. Marshal derives both from the data the run recorder stores, so they always match the run records.

## Export

Set `telemetry.otlpEndpoint` in `marshal.yaml` to export over OTLP gRPC, for example `http://otel-collector:4317`. Without it, Marshal emits the instruments but exports nothing. See [`instance`, `paths`, `logging`, `telemetry`](../configuration/marshal-yaml/instance.md).

| Name | Kind |
|---|---|
| `Marshal.Runs` | Activity source (traces) |
| `Marshal` | Meter (metrics) |

Traces and metrics carry ids and metadata only, never message content, tool arguments or results.

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
| `decision` | `safe`, `pending`, `confirmed_by_keyword`, `judge_positive`, `judge_neutral`, `judge_negative`, `escalated_by_taint`, `denied`, `expired` (see [risk tiers](risk-tiers.md#guard-decisions)) |

Marshal records `marshal.runs` and `marshal.run.cost` when a run completes. It records `marshal.model.tokens` and `marshal.tool.calls` as each step is recorded.

## Traces

Each completed run exports one trace. The root span is `marshal.run`. It has one child span per recorded step. Spans start at the recorded start time of the run or step, so durations reflect the real work.

### Spans

| Span | Step kind |
|---|---|
| `marshal.run` | The whole run. |
| `marshal.context` | Context building. |
| `marshal.model_call` | A call to a model. |
| `marshal.tool_call` | A tool call, after the guard. |
| `marshal.reply` | The final reply. |

### Tags on `marshal.run`

| Tag | Description |
|---|---|
| `marshal.run_id` | The run id. Matches the run in the web UI. |
| `marshal.source` | `voice`, `web`, `task`, `api` or `automation`. |
| `marshal.input_mode` | `text` or `voice`. |
| `marshal.trigger` | What started the run, for example `task`, `continuation`, `confirmation`, `delegate` or `memory_summarizer`. |
| `marshal.outcome` | `ok`, `error`, `backgrounded` or `cancelled`. |
| `marshal.outcome_reason` | The reason for the outcome, when there is one. |
| `marshal.tainted` | `true` when the run read untrusted content. |

### Tags on step spans

| Tag | Description |
|---|---|
| `marshal.sequence` | The step's position in the run. |
| `marshal.model` | The model key, for model calls. |
| `marshal.tier` | The model tier, for model calls: `fast`, `standard` or `advisor`. |
| `marshal.tool` | The internal tool id, for tool calls (see [tools](tools.md)). |
| `marshal.guard_decision` | The guard decision, for tool calls. |
| `marshal.tokens_in` | Input tokens. |
| `marshal.tokens_out` | Output tokens. |

Tags without a value are omitted.

## Example queries

| Question | Query idea |
|---|---|
| Are runs failing? | Rate of `marshal.runs{outcome="error"}`. |
| Did a config change block tools? | Rate of `marshal.tool.calls{decision="denied"}`. |
| How much do I spend per run? | Percentiles of `marshal.run.cost`. |
| Which tool is slow? | Duration of `marshal.tool_call` spans grouped by `marshal.tool`. |

## Related

- [Observability guide](../guides/observability.md)
- [Health endpoints](health-endpoints.md)
- [Risk tiers](risk-tiers.md)
