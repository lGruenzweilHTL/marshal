# Proposal

## Why

The web chat forgets everything on reload: messages live only in the view, and nothing lists earlier conversations. Voice turns from Home Assistant satellites are recorded as sessions and runs but are only visible as raw runs in the Runs view. When a text turn moves to the background (latency cap or `agent.move_to_background`), the chat shows "working on it" and the real answer later lands only in the Inbox, away from the conversation it answers. The data is already recorded (sessions, runs, context and reply steps), so the chat can show it.

## What Changes

- New owner-scoped read API for conversations: a paged list of sessions and one session with its turns (input, reply, input mode, satellite, area, time, artifacts, outcome) and the background results that belong to it.
- Chat history in the web UI: a list of earlier conversations, newest first, with voice conversations marked (input mode, satellite or area). Opening a web conversation continues it. Voice and API conversations are read-only.
- Web sessions no longer close after the idle TTL; they rest instead. Resting has the same effects as closing: pending actions are dropped, project activation is forgotten and memory summarization runs. The session can still be continued at any time. The TTL keeps closing token sessions (voice, API).
- A background task created by a handoff records the session it came from. When it produces its result, the result is stored with that session, published on the live-event stream, and shown in the open chat as a new reply. It also counts as conversation history for the next turn of that session. The Inbox delivery stays unchanged.
- The full reply text of each turn is kept for history (no truncation for reply steps).

## Capabilities

### New Capabilities

- `chat-history`: the conversations read API, what a turn and a background result contain, and the history list and read-only conversations in the web UI.

### Modified Capabilities

- `sessions-identity`: idle close applies to token sessions only. Web sessions rest instead and stay continuable.
- `memory-consolidation`: summarization also runs when a web session rests, reads only turns not summarized before, and counts background results as replies.
- `conversation-api`: the latency cap and self-backgrounding link the task to the originating session, and its result is added to that session.
- `web-chat`: the chat loads a conversation from history, continues web sessions after reload, and shows background results in place.

## Non-goals

- Continuing a voice or API conversation from the web UI (read-only by decision).
- Search, rename, delete or export of conversations.
- Changing how voice or push deliver background results.
- Suppressing the Inbox notification while the chat is open.
- Multi-user sharing of conversations.

## Impact

- `src/Marshal.Data`: migration adding `origin_session_id` to tasks and `rested_at` / `summarized_until` to sessions; owner-scoped conversation queries; history loader includes background results; recorder keeps full reply content.
- `src/Marshal.Core`: session resolver and maintenance rest web sessions instead of closing them; handoff passes the session id.
- `src/Marshal.Memory`: summarizer reads from the session's summary watermark.
- `src/Marshal.Tasks`: `TaskHandoff` sets the origin session; result completion publishes a session event.
- `src/Marshal.Api`: `GET /api/v1/conversations`, `GET /api/v1/conversations/{id}`, live topic `conversations`.
- `web/src/chat`: history list (desktop side panel, phone separate screen), conversation loading, live subscription; route `/chat/:id`.
- `docs/diagrams/erd.puml` (new column), docs for chat.
