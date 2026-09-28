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
