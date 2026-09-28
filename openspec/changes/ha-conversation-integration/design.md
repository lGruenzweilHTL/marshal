## Context

Brainstorm notes §6 define the inbound contract; `core-agent` implements it. HA runs STT before the conversation agent, so Marshal only receives text. HACS requires `custom_components/<domain>/` and `hacs.json` at the repository root; the component lives in `ha/` of the monorepo for now.

Several HA API details are marked "verify when building" in the notes: streaming support for custom conversation entities, how to detect STT vs typed input, and whether `extra_system_prompt` from `assist_satellite.start_conversation` reaches custom agents.

## Goals / Non-Goals

**Goals:**
- Minimal, well-tested component that only speaks the public API.
- Streaming from day one where HA supports it.

**Non-Goals:**
- Exposing HA data to Marshal (Marshal pulls it via MCP / REST).
- Repository split and HACS default-store submission.

## Decisions

### D1. Conversation entity with chat log streaming
Implement a `ConversationEntity` subclass using HA's current chat-log API (`_async_handle_message(user_input, chat_log)`), with streaming enabled and deltas added through the chat log's delta stream. Verify the exact API names against the HA version current at build time and pin `homeassistant` minimum in `manifest.json` / `hacs.json`.
*Alternative:* legacy `AbstractConversationAgent` — rejected, no streaming.

### D2. HTTP client
`aiohttp` via HA's shared client session; a small `MarshalClient` class (`get_versions()`, `stream_turn(request)`) parses SSE lines itself (no extra dependency). Timeouts: connect 5 s, total slightly above Marshal's text latency cap.

### D3. Input mode detection
Primary: the pipeline run's STT stage (voice) vs no STT (typed). If the conversation input carries no reliable signal, fall back to `satellite_id` present → voice, else text. Decide after verifying what `ConversationInput` exposes; covered by tests either way.

### D4. Follow-up marker
Parse `extra_system_prompt` with a strict regex `marshal-followup:([A-Za-z0-9_-]{8,64})`. If HA does not pass `extra_system_prompt` to custom agents, record it; Marshal then uses the fallback in `background-work` (satellite-keyed reply window).

### D5. Interfaces
Python side, kept small: `MarshalClient` (HTTP), `TurnMapper` (HA input → Marshal request). No model-facing tools.

## Risks / Trade-offs

- [HA conversation APIs change between releases] → minimum HA version pinned; CI runs tests against the pinned and latest HA test package.
- [No reliable STT signal] → fallback heuristic; wrong mode only affects output formatting.
- [Streaming unsupported on the pinned HA version] → collect the stream and return the final speech (same stream, non-streaming consumer).

## Migration Plan

Install through HACS (custom repository) or copy into `custom_components`. Configure the Assist pipeline to use the Marshal agent with "prefer handling commands locally" off.

## Open Questions

- Final location of the component (own repository vs monorepo mirror) — decided before publishing to HACS.

## Implementation Notes

Verified against the Home Assistant source of release 2026.9.4 (the version bundled with
`pytest-homeassistant-custom-component` 0.13.367) and the current developer docs.

- **Chat-log streaming (D1).** `ConversationEntity.async_process` opens the chat session and chat log and calls
  `_async_handle_message(user_input, chat_log)`. Streaming entities set `_attr_supports_streaming = True` and feed
  `chat_log.async_add_delta_content_stream(agent_id, stream)` with `{"role": "assistant"}` followed by
  `{"content": "..."}` deltas; the pipeline's delta listener forwards them to TTS when the TTS engine supports
  streaming input. Core integrations (Ollama, OpenAI, Anthropic) pass `self.entity_id` as agent id and register the
  entity with `conversation.async_set_agent` / `async_unset_agent`. The developer docs describe
  `_async_handle_message` and `ConversationResult` but not streaming; the source is authoritative here.
- **Result.** `ConversationResult(response, conversation_id, continue_conversation)`. HA's helper
  `async_get_result_from_chat_log` derives `continue_conversation` from a trailing question mark, so the component
  builds the result itself: speech = Marshal's final `speech`, `continue_conversation` = Marshal's flag,
  conversation id = the chat log's id.
- **`ConversationInput` fields:** `text`, `context` (`Context.user_id` = HA user), `conversation_id` (may be
  `None` outside the pipeline; the chat session always provides one), `device_id`, `satellite_id`, `language`,
  `agent_id`, `extra_system_prompt`.
- **`extra_system_prompt` reaches custom agents (D4).** `assist_satellite` stores the `extra_system_prompt` of
  `start_conversation` (or, if none, the start message) and passes it to the next pipeline run as
  `conversation_extra_system_prompt`, which the pipeline puts into `ConversationInput.extra_system_prompt`. No
  satellite-keyed fallback is needed for the marker.
- **STT vs typed (D3).** `ConversationInput` carries no speech-to-text flag. The pipeline run that calls the agent
  shares its `Context` object with the `ConversationInput`, and `assist_pipeline` keeps running runs in
  `hass.data[KEY_ASSIST_PIPELINE].pipeline_runs`. The component finds the run whose context id equals the input's
  and reports `voice` when its `start_stage` is `wake_word` or `stt`, `text` when it started at `intent`. That
  registry is internal API, so any lookup failure (attribute renamed, no run found, e.g. a `conversation.process`
  service call) falls back to D3's heuristic: `satellite_id` present → `voice`, otherwise `text`.
- **HACS layout.** HACS requires `custom_components/<domain>/` at the repository root (one integration per
  repository) and `version` in `manifest.json`. From this monorepo the component installs by copying
  `ha/custom_components/marshal`; HACS installation needs the planned standalone repository or mirror (open
  question). `hacs.json` is kept ready for that repository.
