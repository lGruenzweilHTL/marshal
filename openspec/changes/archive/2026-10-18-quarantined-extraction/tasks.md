# Tasks

## 1. Extractor

- [x] 1.1 Write failing `ModelQuarantinedExtractor` tests with `FakeChatClient`: no tools in the request; content inside the untrusted wrapper; JSON Schema per answer type; `unknown` accepted for every type; date/datetime parsing; enum values from the caller only; one repair attempt then `extract.invalid_answer`; `text` capped at 500 characters; taint flag false for typed answers and true for `text`; calls recorded as model-call steps. Verify they fail.
- [x] 1.2 Implement `IQuarantinedExtractor` / `ModelQuarantinedExtractor`, reusing the JSON Schema validator from `ha-ai-task`. Verify the 1.1 tests pass.

## 2. Content resolvers and `agent.extract`

- [x] 2.1 Write failing resolver tests for mail (message id, owner-scoped, taint source `mail`), RSS (article id, full text if available, taint `rss`) and notes (path, taint `notes`), each returning `extract.not_found` for unknown or foreign ids. Implement `IUntrustedContentResolver` and the three resolvers. Verify the tests pass.
- [x] 2.2 Write failing tool tests: `agent_extract` resolves the reference and returns only the answer; typed answers leave the run untainted (a following `persists` call executes); `text` taints with the resolver's source and is wrapped; unknown kind fails with `extract.not_found`; hidden without resolvers. Implement the tool. Verify the tests pass.
- [x] 2.3 Add `agent.extract` to the `Marshal.Demo` flows where demo mail, RSS or notes exist (simulated resolvers). Verify the demo tests pass.

## 3. Task tool policies

- [x] 3.1 Write failing EF tests for `tasks.tool_policy` (round trip; null for legacy tasks). Add the column, the migration and `erd.puml`; re-render the SVG. Verify the tests pass.
- [x] 3.2 Write failing `DefaultTaskToolPolicy` tests: derivation from template allowlist, core namespaces and the session's loaded namespaces; template allowlist for sessionless creation; narrowing `tools` accepted; widening rejected; legacy null = template allowlist. Implement. Verify the tests pass.
- [x] 3.3 Write failing tool tests: `tasks_create` and `tasks_watch` store the policy and accept `tools`; `tasks_update` with `tools` fails validation; the confirmation reason for task creation lists the policy. Implement. Verify the tests pass.
- [x] 3.4 Write failing runner tests: a task run's tool list contains only policy tools; the guard denies a call outside the policy; subagents get the intersection; `find_tools` never names a namespace outside the policy. Implement the execution context field, filter and guard rule. Verify the tests pass and the guard branch-coverage check passes (`--coverage`).
- [x] 3.5 Write failing API tests for `PUT /api/v1/tasks/{id}/tool-policy` (cookie only, validated against the template, audited). Implement. Verify the tests pass.
- [x] 3.6 Regenerate the web client; write failing component tests for the policy display and editor in the task detail and a "widen policy" link from a run step denied by the policy; implement with German and English strings. Verify `npm test` and `npm run build` pass.

## 4. Documentation

- [x] 4.1 Document the extractor, `agent.extract`, the taint rule by answer type and task tool policies in the security concept page and the tasks guide. Verify the pages follow `docs/development/docs-style.md`.

## 5. Integration check

- [x] 5.1 Run `dotnet test`, `npm test`, `npm run build` and `openspec validate quarantined-extraction --strict`. Verify all pass.
