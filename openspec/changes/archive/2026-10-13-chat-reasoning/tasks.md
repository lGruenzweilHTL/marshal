# Tasks

## 1. Provider spike

- [x] 1.1 Check the current docs of Anthropic extended thinking, Ollama `think`, and llama.cpp / vLLM `reasoning_content`, and write one in-process fake-server test per provider client (host test project) that sends a reasoning request and asserts `TextReasoningContent` updates arrive; record per provider "works as is" or "needs adapter" in `design.md`
- [x] 1.2 For each provider marked "needs adapter", write the failing fake-server test, then implement the `DelegatingChatClient` adapter in Marshal.Api; verify the tests pass and that a provider without reasoning support logs the startup warning

## 2. Configuration and request

- [x] 2.1 Write failing tests for `ReasoningRequest.For`: `off` and voice turns produce no `Reasoning`, text turns and background runs produce the configured effort with full output, and a missing key means `off`; implement `ModelOptions.Reasoning` and the helper, and verify they pass
- [x] 2.2 Add `reasoning` to `deploy/config.example/marshal.yaml` and the configuration reference docs; verify config validation rejects an unknown value with a startup error naming the key

## 3. Runner events and transcript

- [x] 3.1 Add `ReasoningEvent`, `ToolEvent` and `ToolEventState` to Marshal.Abstractions
- [x] 3.2 Write failing `AgentRunner` tests with a `FakeChatClient` that streams reasoning and a tool call: reasoning events come before deltas, the tool emits `running` then `ok` with the same id, a denied call ends with `denied`, a frozen call ends with `pending`, and the transcript of the next model call keeps the reasoning content; implement and verify they pass
- [x] 3.3 Write failing tests for `DefaultToolCallSummarizer` (argument cutting, first-line result, error code, no result for sensitive and `RedactTools` matches, sharing the recorder's redaction check); implement and verify they pass

## 4. Recording

- [x] 4.1 Write failing recorder tests: a model-call step stores reasoning, and a model call after a redacted tool result stores `reasoningWithheld` and no text; implement in `AgentRunner` and `DbRunRecorder` and verify they pass
- [x] 4.2 Write a failing test that logs from a run with reasoning contain no reasoning text; fix if needed and verify it passes

## 5. API stream

- [x] 5.1 Write failing `WebApplicationFactory` tests: a cookie turn's stream carries `reasoning` and `tool` events; a token turn with the same fake model carries neither, and its event sequence equals today's; implement the SSE mapping with DTOs `TurnReasoning` and `TurnTool`, and verify they pass
- [x] 5.2 Extend the conversation reader (from `chat-history-and-background`) with reasoning, the withheld marker and tool steps from the same summarizer; write the failing reader test first and verify it passes; regenerate OpenAPI and web types and verify `npm run build` passes

## 6. Web UI

- [x] 6.1 Add reasoning and tool events to the preview fake server's turn stream and conversation detail; verify preview mode streams them
- [x] 6.2 Write failing component tests for `ReasoningBlock.vue` (open while streaming, collapsed after the first delta, toggles, renders markup as literal text and loads no image) and implement it; verify they pass
- [x] 6.3 Write failing component tests for `ToolTimeline.vue` (one line per call id updated in place, states, durations, status lines in the same list, label from `chat.tools.<id>` with tool id fallback) and implement it; verify they pass
- [x] 6.4 Wire both into `ChatView` (thinking indicator only until the first event; reopened conversations show both collapsed) with failing tests first; verify they pass
- [x] 6.5 Show reasoning collapsed per model call, or the withheld note, in the run detail, with a failing test first; verify it passes
- [x] 6.6 Check the new UI with the `web-ui-polish` screenshot sweep (375 px and 1280 px, light and dark), add German and English texts, and document reasoning in the chat and configuration docs; verify the docs follow `docs/development/docs-style.md`

## 7. Integration check

- [ ] 7.1 Run the full .NET suite and `npm test` / `npm run build`; then with a real reasoning model configured locally (`reasoning: low`), send a web turn that uses a tool and a voice turn through the API, and confirm the web stream shows reasoning and tool steps and the voice stream does not
  - Done: full .NET suite (16 projects), `npm test`, `npm run build`. Not done: the check against a real reasoning model; none is available locally. The fake-model API tests cover the web and token streams.
