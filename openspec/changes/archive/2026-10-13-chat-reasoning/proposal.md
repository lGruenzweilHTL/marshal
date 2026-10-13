# Proposal

## Why

While Marshal works on a text turn, the chat shows only "Thinking…" until the first reply text arrives. Models with a reasoning mode (Claude extended thinking, Qwen 3 and other thinking models on Ollama or llama.cpp) produce reasoning that would show what Marshal is doing and why. Tool calls are not visible in the chat at all, except as a single latency status line. A visible trace makes long turns understandable, and it makes it easier to see when the model takes a wrong path.

## What Changes

- New per-model setting `reasoning: off | low | medium | high` (default `off`). Text turns and background task runs request reasoning at that effort. Voice turns always request none, because of the voice latency cap.
- The turn stream gains two event types for cookie (web UI) clients only: `reasoning` (reasoning text chunks) and `tool` (a tool call started or finished: display name, short argument summary, outcome, duration, short result summary). Token clients (Home Assistant, API) receive the stream exactly as today.
- The chat shows a collapsible reasoning block per reply, open while it streams and collapsed when the reply text starts. It also shows a timeline of tool steps in place of the plain status lines. "Thinking…" stays only until the first event of any kind.
- Reasoning is recorded on model-call steps and shown in the run detail and in reopened conversations. Reasoning that follows a sensitive tool result in the same run is not stored. Results of sensitive tools are never summarized in the `tool` event.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `model-routing`: models gain the `reasoning` setting and the rule for which runs request it.
- `conversation-api`: `reasoning` and `tool` stream events for cookie principals.
- `run-recording`: model-call steps record reasoning, with the redaction rule.
- `run-insights`: the run detail shows reasoning.
- `web-chat`: reasoning block and tool timeline.
- `chat-history`: turn entries carry reasoning and tool steps. This capability is created by `chat-history-and-background`, so archive that change first.

## Non-goals

- A per-chat "think harder" toggle in the UI.
- Reasoning on voice turns, in spoken replies or in push notifications.
- Sending reasoning to token clients, or a new API version for them.
- Translating or summarizing reasoning text.
- Showing subagent (advisor) reasoning as its own block. Its tool step shows in the timeline.

## Impact

- `src/Marshal.Core`: `ModelOptions.Reasoning`, `AgentRunner` (request options, streaming of `TextReasoningContent`, tool events), `TurnEvent` additions in Marshal.Abstractions.
- `src/Marshal.Api`: provider mapping where `ChatOptions.Reasoning` is not used by a provider; SSE mapping for cookie principals only.
- `src/Marshal.Data`: step payload field `reasoning`; conversation reader exposes reasoning and tool steps.
- `web/src/chat`, `web/src/runs`: reasoning block, tool timeline, run detail.
- `deploy/config.example/marshal.yaml` and the configuration reference docs (`reasoning` key).
- Needs verification against current provider docs: Anthropic extended thinking through the official SDK's `IChatClient`, Ollama `think` through OllamaSharp, llama.cpp / vLLM `reasoning_content` through `Microsoft.Extensions.AI.OpenAI`.
