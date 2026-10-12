# Design

## Context

- `sessions` holds `source`, `created_at`, `last_turn_at`, `closed_at`. `runs` holds `session_id`, `input_mode`, `satellite_id`, `area_id`, `outcome`. `run_steps` of kind `Context` carry the user input (`payload.input`), and steps of kind `Reply` carry the reply (`payload.content`, `continueConversation`, `artifacts`). `EfConversationHistory` already rebuilds a session's chat messages from these two step kinds.
- `DbRunRecorder.Redact` truncates every step's `Content` to `MaxContentLength` (8000), including the reply.
- `SessionResolver.ResolveOwnSessionAsync` returns `session.not_found` for an idle web session. `SessionMaintenance.CloseIdleAsync` closes all idle sessions. Closing drops pending actions and calls the `ISessionCloseListener`s. The only listener is `SessionSummaryInbox`: it forgets project activation and queues summarization. `LlmMemorySummarizer` loads the session's last `MaxTurns` turns through `IConversationHistory`.
- `TaskHandoff` creates the continuation task with `OriginSatelliteId` and an Inbox delivery for text turns. It does not record the session. `MarshalExecutionContext.SessionId` is available there.
- The live-event stream (`/api/v1/events`) multiplexes topics registered as `LiveTopic`, filtered by owner.

## Goals / Non-Goals

**Goals:**
- One read model for conversations that serves the web UI, the model's history and the summarizer, so all three see the same turns and background results.
- Web sessions that can be continued indefinitely, without losing the memory and safety effects of idle close.

**Non-Goals:**
- A new storage table for messages. Turns stay derived from runs and steps.
- Paging inside one conversation. Sessions are small; the detail returns all entries.

## Decisions

**Turns stay derived from runs; background results come from tasks.** A conversation entry is either a run of the session (Context plus Reply step) or a task with `origin_session_id = session` and a result (`result_body`, `result_at`). No `messages` table. Alternative: write a message row per turn and result. Rejected: it duplicates what the recorder already stores, and would need a backfill for existing sessions.

**New interface `IConversationReader`** (Marshal.Abstractions, implemented by `EfConversationReader` in Marshal.Data): `ListAsync(before, limit)` and `GetAsync(sessionId)`, owner-scoped through the existing query filters. Turns are runs with a user-turn trigger (`turn`, `followup`, `confirmation`); the summarizer's own runs (`memory_summarizer`) share the session id and are not turns. One internal query (`ConversationEntries`) serves the reader and the history. `IConversationHistory` (the model's history) stays the interface the agent uses. Its EF implementation is rebuilt on the same query, so it gains background results as assistant messages. The summarizer uses it as before.

**Reply steps are not truncated.** `Redact` skips truncation for `RunStepKind.Reply`. A reply is model output that the user already saw in full, and the redaction policy only concerns tool results. Replies of existing runs that were already truncated keep the marker. Alternative: a separate full-text column. Rejected: not needed.

**Rest instead of close for web sessions.** New nullable columns on `sessions`: `rested_at` and `summarized_until` (timestamp watermark). `SessionMaintenance` handles idle sessions by source:
- `voice` / `api`: close, as today.
- `web` without `rested_at`: drop pending actions, set `rested_at`, call the listeners (unchanged interface `ISessionCloseListener`, renamed in meaning only: "session went idle"). The listeners run the same way for close and rest.

`ResolveOwnSessionAsync` accepts a rested or idle web session: an idle web session that has not rested yet is rested first (same effects), then the session is touched, and `rested_at` is cleared. A closed web session (closed before this change) is still `not_found`.

**Summary watermark.** `LlmMemorySummarizer` reads `summarized_until` through a small `ISummaryWatermark` interface, asks `IConversationHistory` for entries after it (new `after` parameter), then sets the watermark to the newest entry it read. The deterministic chunk id stays the protection against duplicates when two rests overlap.

**Origin session on tasks.** New nullable column `agent_tasks.origin_session_id`. `NewTask.OriginSessionId` is set by `TaskHandoff` from `context.SessionId` for every handoff: voice turns too, so read-only voice conversations show their results. Deliveries stay unchanged.

**Live event from the task store.** `LiveTaskStore` already publishes `task.changed` when a task is saved. When a save sets a new result time on a task with an origin session (the previous value is read before the update), it also publishes `conversation.result` on the new topic `conversations` (constant in a `ConversationLiveEvents` class in Marshal.Tasks, registered as a `LiveTopic`). The payload is `{ sessionId, taskId, resultAt, body }`, and the TypeScript type goes into `web/src/live/events.ts`.

**API.** `GET /api/v1/conversations?cursor=&limit=` and `GET /api/v1/conversations/{id}`, `Policies.Human`, tag `Conversations`, registered on the versioned group. Paging uses `before` (a `lastTurnAt`) plus `limit`, like `/runs`, instead of an opaque cursor. Entry DTO: a discriminated `kind` (`turn` | `backgroundResult`, records `TurnEntry` and `BackgroundResultEntry`) so the generated client types stay exact. Title: the first Context input, trimmed and cut to 80 characters after loading (the input lives in the step's JSON payload).

**Web UI.** Route `/chat/:id?`, the same `ChatView`. A new `ConversationList.vue` component lives in the chat feature folder. On desktop it is a side column inside the chat page. On phones it is a separate screen, opened from a header action. `ChatView` loads the detail when `id` is set, maps entries to its `Message` type (voice turns get `inputMode` and `satellite` for the mark, background results get a `background` flag), and subscribes to the `conversations` topic through the existing `liveEvents` composable. `LiveEvents` gains `onReconnect` (fired when the `EventSource` opens again after an error); on reconnect the chat reloads the detail. The phone list screen is the route `/chat/history`. After the first final event of a new chat it does `router.replace` to `/chat/{id}` and refreshes the list. Read-only sessions (`canContinue: false`) hide the composer.

**No model-facing tools are added or changed.**

## Risks / Trade-offs

- [Never-closing web sessions grow long] → The model history is already capped at the last `MaxTurns` turns, and the UI detail shows all of them. That is acceptable for single-user sizes.
- [Rest effects surprise the user: a pending confirmation vanishes after 5 minutes] → Same as today for every session. The confirmation has its own shorter TTL anyway.
- [Rename of meaning for `ISessionCloseListener`] → Keep the name to avoid churn, and document in its XML comment that it fires on close and on rest.
- [Old web sessions closed before the change cannot be continued] → They are shown in history as read-only (`canContinue` is false when `closed_at` is set).
- [Two tabs on the same session] → Both receive the live event and both reload. Turns stay ordered by run start.

## Migration Plan

One EF migration: `sessions.rested_at`, `sessions.summarized_until`, `agent_tasks.origin_session_id` (all nullable, with an index on `origin_session_id`). No backfill. Rollback: the columns are ignored by older code. Update `docs/diagrams/erd.puml` and re-render it.
