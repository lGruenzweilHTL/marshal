# Design

## Context

- `AgentRunner.CallModelAsync` streams `GetStreamingResponseAsync` and forwards `update.Text` as `DeltaEvent`. It sets only `ChatOptions.Tools`. Reasoning content (`TextReasoningContent`) in updates is ignored today. `update.Text` excludes it.
- `Microsoft.Extensions.AI.Abstractions` 10.10.1 has `ChatOptions.Reasoning` (`ReasoningOptions { Effort, Output }`) and `TextReasoningContent`. The three provider packages in use (Anthropic 12.50.0, OllamaSharp 5.4.30, `Microsoft.Extensions.AI.OpenAI` 10.10.1) all reference these types. Whether each one maps them for our cases is not verified yet. The open case is the non-standard `reasoning_content` field of llama.cpp and vLLM over chat completions.
- The only `StatusEvent` today is the latency-cap status. Tool calls are recorded as steps but are not streamed.
- `ToolDescriptor` has no display label. The tool id is the stable name.
- Turn events go through `ChannelWriter<TurnEvent>`. `ConversationEndpoints.StreamAsync` maps them to SSE items and knows whether the principal is a cookie or a token.

## Goals / Non-Goals

**Goals:**
- Reasoning requested only where latency allows it, streamed to the web UI, recorded, and redacted like tool results.
- A visible tool timeline for web turns.
- Token clients unaffected, with no new API version.

**Non-Goals:**
- Provider-specific reasoning features beyond effort (budgets in tokens, interleaved-thinking flags), unless the spike shows effort alone does not work.

## Decisions

**Effort through `ChatOptions.Reasoning`.** `ModelOptions.Reasoning` (`off | low | medium | high`) maps to `ReasoningOptions { Effort, Output = Full }`. `off` and voice turns map to no `Reasoning` property at all, not to `Effort = None`. That avoids sending a thinking flag to models that reject one. A small helper (`ReasoningRequest.For(model, context)`) in Marshal.Core decides this, and is unit-tested.

**Spike first: provider verification.** Before the runner changes, a task checks each provider against its current docs and an in-process fake HTTP server:
- Anthropic: extended thinking (`thinking` blocks) arrives as `TextReasoningContent`, and the signature survives in the transcript for the next tool round.
- Ollama: `think` arrives as reasoning content.
- OpenAI-compatible: `reasoning_content` deltas arrive.

If a provider does not map them, a `DelegatingChatClient` in Marshal.Api (host only, where provider SDKs live) adapts that provider: it sets the provider's own request option and turns the provider's raw reasoning field into `TextReasoningContent`. The outcome is recorded in this design file before the runner work starts. Alternative: parse `<think>` tags from text. Accepted only as the fallback for OpenAI-compatible servers that inline reasoning into content, and only inside that adapter.

**Reasoning must round-trip in the transcript.** The runner keeps `TextReasoningContent` in the assistant messages of the running transcript (Anthropic requires signed thinking blocks before tool results). It strips reasoning from history loaded from earlier turns and from background handoff transcripts that change model. The history only rebuilds input and reply text anyway.

**New turn events in Marshal.Abstractions:** `ReasoningEvent(string Text)` and `ToolEvent(string CallId, ToolId Tool, string Arguments, ToolEventState State, int? DurationMs, string? Result)`. The runner writes them for every interactive run. `ConversationEndpoints.StreamAsync` drops them for token principals, which is one place to test the "unchanged for token clients" rule. Alternative: the runner skips them for token clients. Rejected: the runner should not know about principal kinds.

**Summaries are built server-side by `IToolCallSummarizer`** (new small interface in Marshal.Core, v1 `DefaultToolCallSummarizer`). It renders arguments as `key=value` pairs, with strings cut to 60 characters and 120 characters in total. A result becomes its first line, cut to 120 characters, or an error code. It returns no result for tools that are sensitive or matched by the recorder's `RedactTools` (the redaction check is shared with `DbRunRecorder`). Summaries are plain text. The UI never renders them as markdown.

**Recording.** `StepPayload` gains `reasoning` (string, truncated to `MaxContentLength`) and `reasoningWithheld` (bool). The runner tracks "a redacted tool result is in this run's transcript". Once it is set, later model-call steps store `reasoningWithheld = true` instead of the text. Tool steps already hold args and results. The conversation reader derives tool steps for `chat-history` from them, through the same summarizer, so live and reopened views match.

**Web UI.** `Message` gains `reasoning: string`, `reasoningOpen: boolean` and `steps: ToolStep[]` (status lines become steps of kind `status`). A new `ReasoningBlock.vue` (`<details>`, plain text in a `<pre>`-like block with wrapping, muted tokens) and a new `ToolTimeline.vue` in `web/src/chat`. Tool labels come from an optional i18n map `chat.tools.<id>`, with the tool id as fallback. Run detail (`web/src/runs`) shows reasoning in a collapsed `<details>` per model call.

**No model-facing tools are added or changed.**

## Spike outcome (task 1.1)

Fake-HTTP tests in `tests/Marshal.Api.Tests/Models/ProviderReasoningTests.cs`, against the SDK versions in `Directory.Packages.props`:

- **Ollama (OllamaSharp 5.4.30): works as is.** `ChatOptions.Reasoning` sets the request's `think` flag; streamed `message.thinking` arrives as `TextReasoningContent`.
- **OpenAI-compatible (`Microsoft.Extensions.AI.OpenAI` 10.10.1): works as is.** The effort is sent as `reasoning_effort`; streamed `reasoning_content` deltas (llama.cpp `--reasoning-format`, vLLM reasoning parsers) arrive as `TextReasoningContent`. Servers that ignore `reasoning_effort` still return reasoning when their template thinks; servers that inline `<think>` tags into content are not adapted (no such server in use).
- **Anthropic (Anthropic 12.50.0): works as is for Claude 4.6 and later.** The SDK sends `thinking: { type: "adaptive", display: "summarized" }` plus `output_config.effort`; thinking deltas arrive as `TextReasoningContent` with the signature in `ProtectedData`, and a transcript that keeps that content sends the signed thinking block back unchanged (verified). Adaptive thinking needs Claude 4.6 or later; Haiku 4.5 and older models only take `budget_tokens`. For those models Marshal logs the startup warning and calls them as `off`; no budget adapter is built.

No `DelegatingChatClient` adapter is needed (task 1.2 reduces to the startup warning, implemented with the request helper).

## Risks / Trade-offs

- [A provider does not map reasoning] → Spike first; adapter in the host. If a provider cannot do it at all, its models log a startup warning and behave as `off`.
- [Reasoning raises latency and cost of text turns] → Default `off`. The text latency cap (~60 s) still backgrounds long turns. Reasoning tokens are counted in output tokens and priced.
- [Reasoning quotes untrusted content (mail, RSS)] → Rendered as plain text. Withheld from storage after redacted results. It is never fed back into later turns' history.
- [Anthropic thinking signatures and transcript continuation across handoff] → The handoff transcript keeps reasoning content for the same model. The spike covers one tool round-trip.
- [Stream contract for cookie clients grows] → Cookie clients are the bundled SPA, built from the same commit. Token clients' v1 contract is untouched, and a test covers that.

## Open Questions

- Exact `low / medium / high` mapping for Anthropic budget tokens if the SDK needs a budget instead of effort. The spike settles this, and the setting's values stay the same either way.
