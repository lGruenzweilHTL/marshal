# Tasks

## 1. Namespace descriptions and configuration

- [x] 1.1 Write failing options tests for `tools.core` (default list; unknown namespace names only warn on `/health`), `tools.retrieval.*` (defaults 0.30 / 3 / 24 / enabled; out-of-range values fail startup naming the key) and `voice.fillerThresholdMs` (default 700, positive). Implement the options and validators. Verify the tests pass.
- [x] 1.2 Write failing tests that every built-in tool source declares a description for each namespace it uses, and that `McpToolSource` takes `description` from the server file or builds the fallback from id and tool names. Add `IToolSource.Namespaces`, the descriptions and the MCP config key. Verify the tests pass.
- [x] 1.3 Document `tools.*`, `voice.fillerThresholdMs` and the MCP `description` key in the configuration reference and add `description` to `deploy/config.example/mcp/ha.yaml`. Verify the example config loads in the config load test.

## 2. Selection

- [x] 2.1 Write failing `CachedNamespaceEmbeddings` tests with `FakeEmbeddingGenerator`: one embedding per description; cache hit on unchanged descriptions; rebuild after a source reload. Implement. Verify the tests pass.
- [x] 2.2 Write failing `HaNamespaceHints` and `MusicNamespaceHints` tests (entity name, alias, area name → `ha`; speaker name → `music`; nothing for unrelated text; German and English). Implement `IToolNamespaceHints` and both hints. Verify the tests pass.
- [x] 2.3 Write failing `NamespaceToolSelector` tests: core always loaded; retrieval above threshold loads at most `maxPerTurn`, best first; hints load; session-loaded namespaces stay; skipped at or below `minTools` and when disabled; no embedding generator → core plus hints only; filtered tools never appear; order = core order, load order, tool name; loading appends only. Verify they fail.
- [x] 2.4 Implement `IToolSelector` / `NamespaceToolSelector` and wire it after `ToolVisibilityFilter` in the agent runner, reusing the turn embedding from memory retrieval. Verify the 2.3 tests and the existing visibility filter tests pass.
- [x] 2.5 Write failing EF tests for `sessions.loaded_tool_namespaces` (round trip, null = core only). Add the column, the migration (`dotnet ef migrations add ToolRetrieval`) and the `erd.puml` change; re-render the SVG. Verify the tests pass and the SVG is updated.
- [x] 2.6 Write failing runner tests: the loaded set is persisted after a turn and read on the next; subagents start from core. Implement. Verify the tests pass.

## 3. `agent.find_tools` and unloaded calls

- [x] 3.1 Write failing tests: `agent_find_tools` loads the best namespaces among those with visible tools and returns names and descriptions; the next model call includes them; a namespace denied by the satellite is never named; hidden when retrieval is disabled or skipped, or when all namespaces with visible tools are loaded; a call to a visible tool of an unloaded namespace loads it (reason `call`) and runs through the guard. Verify they fail.
- [x] 3.2 Implement `agent.find_tools` (safe, `agent` namespace) and unloaded-call loading. Verify the 3.1 tests pass.

## 4. Context stability

- [x] 4.1 Write failing context builder tests: ordering by stability then order; consecutive turns with different time, memory and area produce identical text up to the first `turn` contribution; the system prompt is returned as a stable part and a turn part. Verify they fail.
- [x] 4.2 Add `IContextContributor.Stability`, assign it to every contributor as in design.md decision 5, and change the builder. Verify the 4.1 tests and all existing context tests pass.
- [x] 4.3 Check whether the `Anthropic` 12.50 `IChatClient` applies prompt caching automatically or needs cache control through `AdditionalProperties`; record the result in design.md decision 5. If needed, set a breakpoint after the stable part in the provider setup in `Marshal.Api`, with a failing test against a fake `HttpMessageHandler` first. Verify the request body carries the breakpoint (or the note explains why none is needed).

## 5. Filler

- [x] 5.1 Write failing `RecordedToolLatencyStats` tests: median of the last 20 durations; null below 5 calls; cached for 5 minutes (`FakeTimeProvider`). Implement `IToolLatencyStats`. Verify the tests pass.
- [x] 5.2 Write failing agent runner tests: a voice turn sends the filler as a `delta` before a slow tool; none for fast tools, text turns, after model text, after a first filler, for a call that becomes a pending action, or when no line exists in the turn language; the filler is part of the recorded reply. Verify they fail.
- [x] 5.3 Add `ToolDescriptor.Fillers`, the emission in the runner, the base prompt sentence about an already spoken filler, and German and English fillers for the slow built-in tools listed in design.md decision 6. Verify the 5.2 tests pass.
- [x] 5.4 Extend `Marshal.Demo` simulations if their descriptors are shared (fillers appear in demo voice flows). Verify the demo tests pass.

## 6. Recording and views

- [x] 6.1 Write failing recording tests: the context step lists loaded namespaces with reasons and scores; `time_to_first_delta_ms` is set for runs with a delta and null otherwise. Add the run column to the migration from 2.5 (or a second migration) and implement. Verify the tests pass.
- [x] 6.2 Expose loaded namespaces and time to first delta in the runs API (v1, additive fields) with failing API tests first. Verify the tests pass and the OpenAPI document contains the fields.
- [x] 6.3 Regenerate the web API client; write failing component tests for the runs list column and sorting and the voice timeline in the run detail (segments, first delta marker), then implement with German and English strings. Verify `npm test` and `npm run build` pass.
- [x] 6.4 Add the new views to the preview scenarios and the screenshot sweep. Verify `npm run screenshots` produces the run detail timeline.
- [x] 6.5 Document tool retrieval, `find_tools` and fillers in the concepts docs (how tools reach the model) and the runs view guide. Verify the pages follow `docs/development/docs-style.md`.

## 7. Integration check

- [x] 7.1 Run `dotnet test`, `npm test`, `npm run build` and `openspec validate tool-retrieval-and-latency --strict`. Verify all pass.
