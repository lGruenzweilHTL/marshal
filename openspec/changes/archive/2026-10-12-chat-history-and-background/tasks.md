# Tasks

## 1. Data model

- [x] 1.1 Add `Session.RestedAt`, `Session.SummarizedUntil` and `AgentTask.OriginSessionId` (with index), create the EF migration, and update and re-render `docs/diagrams/erd.puml`; verify `dotnet build` and the Marshal.Data migration test pass
- [x] 1.2 Write a failing recorder test that a reply step longer than `MaxContentLength` is stored in full, and tool results are still truncated; make `Redact` skip truncation for reply steps and verify the test passes

## 2. Web sessions rest instead of closing

- [x] 2.1 Write failing `SessionMaintenance` tests: an idle voice session is closed; an idle web session is rested (pending actions dropped, listeners called, `closed_at` stays null), and is not rested twice; verify they fail
- [x] 2.2 Implement rest in `SessionMaintenance` and the session store; verify the 2.1 tests pass
- [x] 2.3 Write failing `SessionResolver` tests: a rested web session and an idle, not yet rested web session are both continued (the second one rested first); a closed web session is still `session.not_found`; implement and verify they pass
- [x] 2.4 Update the `ISessionCloseListener` XML comment (close and rest) and the sessions section of the docs; verify the docs follow `docs/development/docs-style.md`

## 3. Conversation reader and history

- [x] 3.1 Define `IConversationReader` and the entry records (turn, background result) in Marshal.Abstractions
- [x] 3.2 Write failing `EfConversationReader` tests: list order and paging, task sessions excluded, owner isolation, title cut to 80 characters, `canContinue` per source and `closed_at`, detail with mixed input modes, artifacts, outcome without reply, background result after its turn; verify they fail
- [x] 3.3 Implement `EfConversationReader` and verify the 3.2 tests pass
- [x] 3.4 Write failing `EfConversationHistory` tests: background results appear as assistant messages in order; the `after` watermark returns only newer entries; rebuild it on the reader query and verify they pass
- [x] 3.5 Write a failing summarizer test for the second rest (reads only new turns, advances `summarized_until`); implement the watermark in `LlmMemorySummarizer` with a fake `IChatClient` and verify it passes

## 4. Background results join the session

- [x] 4.1 Write a failing `TaskHandoff` test that the created task carries the turn's session as `OriginSessionId` (text and voice); implement and verify it passes
- [x] 4.2 Write a failing `LiveTaskStore` test that saving a result on a task with an origin session publishes `conversation.result` on `conversations` for the task's owner only, and a task without an origin session publishes none; implement, register the topic, and verify it passes
- [x] 4.3 Write a failing end-to-end test with a fake chat client: a text turn hits the latency cap, the task completes, then the next turn of that session sees the result in its history; verify it passes after 4.1 and 4.2

## 5. API

- [x] 5.1 Write failing `WebApplicationFactory` tests for `GET /api/v1/conversations` and `/{id}` (cookie user, 403 for tokens, 404 for a foreign or unknown id, cursor paging, entry kinds); implement the endpoints on the versioned group and verify they pass
- [x] 5.2 Regenerate the OpenAPI document and `web/src/api/schema.d.ts`; add the `conversation.result` payload type to `web/src/live/events.ts`; verify `npm run build` passes

## 6. Web UI

- [x] 6.1 Add the conversation list and detail to the preview fake API (`web/src/testing/fakeServer.ts`, preview scenarios) with web, voice and API sessions and a background result; verify preview mode shows them
- [x] 6.2 Write failing component tests for `ConversationList.vue` (order, voice mark with satellite, API mark, empty and error states) and implement it; verify they pass
- [x] 6.3 Write failing `ChatView` tests: `/chat/{id}` loads and renders turns with voice marks; read-only sessions show the note instead of the composer; the first final event of a new chat replaces the URL; a 404 shows "not found" with a new chat action; implement and verify they pass
- [x] 6.4 Write failing `ChatView` tests for live results: a `conversation.result` for the open session appends a marked background reply, one for another session changes nothing, and a reconnect reloads the detail; implement and verify they pass
- [x] 6.5 Add the route `/chat/:id?`, the desktop side column and the phone list screen, with German and English texts; verify the layouts in the `web-ui-polish` screenshot sweep at 375 px and 1280 px, light and dark
- [x] 6.6 Document chat history and background results in the user docs (chat page) and verify the docs follow `docs/development/docs-style.md`

## 7. Integration check

- [x] 7.1 Run the full .NET test suite and `npm test` / `npm run build`; then in preview mode and against a local host, start a text chat, reload it, open a voice conversation, and trigger a backgrounded turn with a fake slow tool; confirm each behavior from the specs
