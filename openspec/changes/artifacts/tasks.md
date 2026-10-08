# Tasks

Dependency order: group 1 first; groups 2, 4, 5, 6 and 7 can run in parallel after it; group 3 after 2; group 8 after 2 and 3; group 9 last.

## 1. Project, entity and contracts

- [x] 1.1 Create `src/Marshal.Artifacts` and `tests/Marshal.Artifacts.Tests`, add them to `Marshal.sln`, and update `docs/diagrams/packages.puml` / `.svg` and the CLAUDE.md repository map. Verify `dotnet build` of the new projects succeeds.
- [x] 1.2 Add `IArtifactStore`, `IArtifactLinks`, `ArtifactRef`, `ArtifactDraft`, `ArtifactQuery` and the kind/source enums to `Marshal.Abstractions`, and `ToolOutput.Artifacts`. Verify the solution builds.
- [x] 1.3 Write a failing `Marshal.Data` test that an `Artifact` row is owner-filtered (another owner's row is invisible). Add the `Artifact` entity with query filter and the migration (`dotnet ef migrations add Artifacts --project src/Marshal.Data --output-dir Migrations`), and update `docs/diagrams/erd.puml` / `.svg`. Verify the test passes.

## 2. Store, kinds and quota

- [x] 2.1 Write failing `ArtifactKindDetector` tests: every kind from its extension plus matching bytes; disguised HTML as `.png` → `other`; binary data as `.md` → `other`; SVG without `<svg` root → `other`; 3MF ZIP detection; STL ASCII and binary. Verify they fail, implement, verify they pass.
- [x] 2.2 Write failing `FileArtifactStore` tests (temp data directory, `FakeTimeProvider`): create stores file and row with hash and size; owner scoping on get, list and delete; delete removes file and row; list filters by kind, source and name; quota exceeded stores nothing and reports usage; per-file limit aborts mid-stream and leaves no temp file; two concurrent writes cannot both pass the quota. Verify they fail, implement, verify they pass.
- [x] 2.3 Add the `artifacts` options (`quotaMb`, `maxUploadMb`, `signedLinkMinutes`) with validation and defaults, and the section in `deploy/config.example/marshal.yaml`. Verify the options tests and the example-config test pass.

## 3. API: upload, content, links, delete

- [x] 3.1 Write failing `Marshal.Api.Tests`: upload creates an artifact (201) and publishes on the `artifacts` live topic; 413 over `maxUploadMb`; quota error as a problem response; list and get are owner-scoped (404 for another owner); delete removes it and publishes on the topic. Verify they fail, add the endpoints and the live topic, verify they pass.
- [x] 3.2 Write failing tests for content serving: `nosniff` and detected media type on every kind; `html` and `svg` carry the sandbox CSP; `other` is an attachment; range requests on video. Verify they fail, implement, verify they pass.
- [x] 3.3 Write failing tests for signed links (`ApiArtifactLinks`): valid within the lifetime without a session; 404 after expiry (`FakeTimeProvider`), with an altered signature, or with a signature for another artifact. Verify they fail, implement, verify they pass.

## 4. Turn stream and tools

- [x] 4.1 Write failing `AgentRunner` tests with a fake `IChatClient` and a fake tool returning `ToolOutput.Artifacts`: one `ArtifactEvent` per ref is written before the final event; the refs are stored with the assistant message. Verify they fail, implement (adding the message metadata column to the group 1 migration if needed), verify they pass.
- [x] 4.2 Write a failing `ConversationEndpoints` test that an `ArtifactEvent` becomes an SSE `artifact` item with id, name, kind and URL. Implement it. Verify it passes.
- [x] 4.3 Add `artifacts` to `ReservedNamespaces`. Write failing tool tests: `artifacts_list` filters and limits to 50, is `UntrustedOutput` with taint kind `artifact`; `artifacts_show` returns the ref for an owned id and not-found otherwise; `artifacts_show` is visible only when `SessionSource` is `Web`. Verify they fail, implement the tool source, verify they pass.

## 5. Executor artifacts

- [x] 5.1 Write failing `Marshal.Api.Tests` for `POST executor/callbacks/{sessionId}/artifacts`: stores a tainted `executor` artifact owned by the task's owner with the task as source reference; 403 with another session's token; 401 after the session ended; size and quota refusals return a reason. Verify they fail, implement, verify they pass.
- [x] 5.2 Write failing `Marshal.ExecutorHost` tests: files under `.marshal/artifacts/` are uploaded before completion; symbolic links and paths escaping the folder are skipped; refused uploads are listed in the completion notes. Verify they fail, implement, verify they pass.
- [x] 5.3 Write a failing end-to-end task test: a fake executor completion with an uploaded STL makes the artifact exist before the `onDoneGoal` run starts. Verify it passes.

## 6. Mail attachments

- [x] 6.1 Write failing IMAP provider tests (Testcontainers IMAP server, `sg docker`): `ReadAttachmentAsync` fetches one attachment by name and by position, and fails for an unknown attachment. Verify they fail, add the method to `IMailProvider` and the IMAP implementation, verify they pass.
- [x] 6.2 Write failing tool tests: `mail_save_attachment` has `DefaultTier = Confirm`, `Persists` and `Sensitive`; hidden without a mail account; confirmed call stores a tainted `mail` artifact with the message reference and returns its id; unknown attachment fails naming the attachments and stores nothing. Verify they fail, implement, verify they pass.

## 7. Printer snapshots

- [x] 7.1 Write a failing `Marshal.Api.Tests` test: `POST /api/v1/printers/{printer}/snapshots/{id}/keep` creates an untainted `printer` image artifact that survives pruning of the snapshot store, and writes an audit event. Verify it fails, implement, verify it passes.

## 8. Web UI

- [x] 8.1 Add `three` and `highlight.js` to `web/package.json` and the CLAUDE.md package index; regenerate the API types (`npm run gen:api`). Verify `npm run build` passes.
- [x] 8.2 Write failing component tests for the Artifacts list: newest first with kind, source, size, time and tainted marker; filters and search; upload with progress; delete only after the confirmation dialog; quota usage; live topic adds and removes rows; German and English texts. Verify they fail, implement the route, navigation entry and list, verify they pass.
- [x] 8.3 Write failing viewer tests per kind: image and video elements; SVG via `<img>`; code highlighted with line numbers; diff via the shared diff view; Markdown with external images as links; HTML in a sandboxed iframe; PDF opens a new tab; `other` shows download only; source and tainted marker on every viewer. Verify they fail, implement, verify they pass.
- [x] 8.4 Write a failing test that the 3D viewer loads an STL and a 3MF fixture and shows controls, and extend the `productionBundle` architecture test so the main chunk contains neither three.js nor highlight.js. Implement the lazy 3D viewer (size prompt over 50 MB). Verify `npm test` and `npm run build` pass.
- [x] 8.5 Write failing chat tests: an `artifact` event of kind `image` renders inline; a `model3d` event renders a card that opens the viewer; cards render again after reopening the session. Implement them. Verify they pass. (Deviation: the web chat has no session reopen yet. The shown artifacts are stored with the run's reply step (`RunStepRecord.Artifacts` in the step payload), but no endpoint returns them yet; the reopen part waits for a session history view.)
- [x] 8.6 Write a failing printer page test for the "Keep" action and its toast. Implement it. Verify it passes.
- [x] 8.7 Add artifact scenarios to the preview mode fixtures (`web/src/preview/scenarios`). Verify the scenarios test passes.

## 9. Integration check

- [x] 9.1 Write an end-to-end API test with a fake `IChatClient`: upload an STL, a web turn calls `artifacts_list` then `artifacts_show`, the stream carries the `artifact` event and the run is tainted with `artifact`. Run the full .NET suite, the web suite (`npm test`, `npm run build`) and `openspec validate artifacts --strict`. Verify all pass.
