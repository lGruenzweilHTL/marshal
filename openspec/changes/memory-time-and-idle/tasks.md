# Tasks

## 1. Valid time in the store

- [x] 1.1 Write failing store tests: default `validFrom` = creation time; `validTo` before `validFrom` fails; in-place update stores the version with `validTo` = new `validFrom`; supersede closes the old chunk; date inputs mean 00:00 in the owner's time zone. Verify they fail.
- [x] 1.2 Add the columns to chunks and versions, the migration with backfill and `erd.puml`; re-render the SVG. Implement the write rules. Verify the 1.1 tests pass.
- [x] 1.3 Write failing NDJSON export tests that chunks and versions carry valid time. Implement. Verify they pass.

## 2. Retrieval

- [x] 2.1 Write failing retrieval tests: closed chunks are excluded from automatic retrieval in both search phases; future `validFrom` chunks are included with their valid time; `memory_recall` without `asOf` searches open chunks; with `asOf` it finds the version valid then (FTS over version text, parent embedding) and returns valid times. Verify they fail.
- [x] 2.2 Implement the filters, version FTS rows (built by the index rebuilder) and `asOf`. Verify the 2.1 tests and existing retrieval tests pass.

## 3. Writing valid time

- [x] 3.1 Write failing tests: `memory_remember` accepts `validFrom` / `validTo` (dates and date-times, validation errors otherwise); the summarizer's candidate schema carries optional valid time and writes it. Implement. Verify the tests pass.

## 4. Proposals and idle-time jobs

- [x] 4.1 Write failing EF tests for `memory_proposals` (owner-scoped; unique pair per kind; dismissed pairs not re-proposed). Add the table, migration and `erd.puml`. Verify they pass.
- [x] 4.2 Write failing tests with `FakeChatClient` and `FakeEmbeddingGenerator`: `ContradictionProposalJob` proposes for open `contradicts` pairs only once; `DuplicateProposalJob` finds same owner/type/scope pairs above `memory.duplicateThreshold` among the day's changed chunks; `ProjectSummaryJob` only runs for changed projects and caps at 120 words; jobs read no sessions or tool results; one job's failure does not stop the others; spend counts against the daily cap. Verify they fail.
- [x] 4.3 Implement `IIdleMemoryJob`, the three jobs and their chaining in `ConsolidationJob`; add `memory.duplicateThreshold` with validation. Verify the 4.2 tests pass.
- [x] 4.4 Write failing tests for accepting and dismissing: contradiction accept closes the outdated chunk and resolves the edge; duplicate accept updates the kept chunk and supersedes the other; both recorded as user actions in versions and audit; dismiss changes nothing. Implement `IMemoryProposals` and the API endpoints. Verify the tests pass.

## 5. Project summaries

- [x] 5.1 Write failing tests: active projects' summaries appear in the context; projects without a summary add nothing; the projects API returns the summary read-only. Add the columns and implement. Verify the tests pass.

## 6. Task preparation

- [ ] 6.1 Write failing scheduler tests (`FakeTimeProvider`): a cron task with `prepare.before: 3h` gets a preparation run 3 h before each occurrence with the preparation goal, template, policy and budget; `before` above 12 h fails validation; prepared notes and taint are stored; the main run sees the notes and starts tainted; notes are discarded after the main run or when stale; a failed preparation does not block the main run. Verify they fail.
- [x] 6.2 Add the task columns to the migration, the `prepare` argument to `tasks.create` / `tasks.update` and the task API, and implement the scheduling and context section. Verify the 6.1 tests pass.
- [ ] 6.3 Add `prepare` to `deploy/config.example/tasks/briefing.yaml` with a comment and update the briefing skill to use prepared notes. Verify the example config loads in the config load test.

## 7. Web UI and docs

- [ ] 7.1 Regenerate the web client; write failing component tests for valid time in the editor, the closed-chunk filter, valid times in history, proposals in the audit (accept / dismiss) and the project summary; implement with German and English strings; add preview scenario data. Verify `npm test`, `npm run build` and the screenshot sweep pass.
- [ ] 7.2 Document valid time, `asOf`, proposals, idle-time jobs and task preparation in the memory and tasks guides and the configuration reference. Verify the pages follow `docs/development/docs-style.md`.
- [ ] 7.3 Extend `Marshal.Demo` seed data with a closed chunk, a version with valid time and one open proposal. Verify the demo seeding tests pass.

## 8. Integration check

- [ ] 8.1 Run `dotnet test`, `npm test`, `npm run build` and `openspec validate memory-time-and-idle --strict`. Verify all pass.
