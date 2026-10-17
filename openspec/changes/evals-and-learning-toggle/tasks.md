# Tasks

## 1. Preference and ratings

- [ ] 1.1 Write failing tests: `helpMarshalLearn` defaults to false, round-trips through `/api/v1/me/preferences`, and is exposed through `IUserPreferences`. Add the column (migration, `erd.puml`) and implement. Verify the tests pass.
- [ ] 1.2 Write failing API tests for `PUT` / `DELETE /api/v1/runs/{id}/rating` (own runs only, cookie principals only, note length limit) and the runs list rating filter. Add the run columns and implement. Verify the tests pass.

## 2. Turn context snapshot

- [ ] 2.1 Write failing recording tests: the context step stores the turn-level contributions' text separately; the retention purge removes it with the run. Implement using the context builder's split output. Verify the tests pass.

## 3. Eval cases

- [ ] 3.1 Write failing tests: rating with the toggle on creates a case in the same transaction (expectation from the rating); toggle off creates none; admin save creates a case regardless (`unrated` without rating); sensitive references mark the case `incomplete`; canonical arguments; cases are owner-scoped and survive the retention purge. Add `eval_cases` (migration, `erd.puml`) and implement. Verify the tests pass.
- [ ] 3.2 Write failing API tests for listing, editing the note, deleting cases (admin) and "save as case" on a run. Implement. Verify the tests pass.

## 4. Replay

- [ ] 4.1 Write failing `RecordedToolExecutor` tests: matching call returns the recorded result; argument order does not matter; repeated identical calls allowed; unmatched call returns `no_recording` and flags divergence. Implement. Verify the tests pass.
- [ ] 4.2 Write failing `AgentLoopReplayer` tests with `FakeChatClient`: history, input and turn context from the case; static context from current configuration with template and base prompt overrides; model per tier from the candidate; fixed clock; no rows written to runs, memory, tasks, approvals or notifications; components that write outside tools skip replay contexts. Implement. Verify the tests pass.
- [ ] 4.3 Write failing comparison tests for `pass`, `changed`, `diverged`, `failed` for good and bad cases, and judge scoring through a fake evaluator. Add `Microsoft.Extensions.AI.Evaluation` and `.Quality` to `Directory.Packages.props` and the `CLAUDE.md` package index; implement. Verify the tests pass.
- [ ] 4.4 Write failing tests for eval runs: created with a candidate config and cost estimate; executed one case at a time on the worker pool; cancellable; cost counted against the daily cap; results and totals stored. Add `eval_runs` / `eval_results` and implement, with admin-only API endpoints. Verify the tests pass and the OpenAPI document has the endpoints.

## 5. Web UI

- [ ] 5.1 Regenerate the web client; write failing component tests for thumbs in web chat (hidden while streaming, note on thumbs down, stored rating shown for earlier conversations), rating and "save as case" in the run detail, the rating filter, and the "help Marshal learn" toggle with its explanation in preferences. Implement with German and English strings. Verify `npm test` passes.
- [ ] 5.2 Write failing component tests for the evals admin view: case list with expectation, incomplete marker and note editing; new eval run form (models per tier, template, base prompt, judge, cost estimate); report with totals and side-by-side calls and replies. Implement with German and English strings and add preview data. Verify `npm test`, `npm run build` and the screenshot sweep pass.

## 6. Documentation

- [ ] 6.1 Document ratings, the toggle, eval cases and replays in a new guide (`docs/guides/evals.md`), including privacy notes and cost, and link it from the documentation home. Verify the page follows `docs/development/docs-style.md`.

## 7. Integration check

- [ ] 7.1 Run `dotnet test`, `npm test`, `npm run build` and `openspec validate evals-and-learning-toggle --strict`. Verify all pass.
