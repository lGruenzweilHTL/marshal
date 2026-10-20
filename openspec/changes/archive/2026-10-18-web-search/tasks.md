# Tasks

## 1. Project and configuration

- [x] 1.1 Create `src/Marshal.Integrations.Web` and `tests/Marshal.Integrations.Web.Tests`, add them to `Marshal.sln`, reference from `Marshal.Api`; update the `CLAUDE.md` repository map and `packages.puml` (re-render the SVG). Verify the solution builds.
- [x] 1.2 Write failing options tests for `web.searxng` (absolute URL, secret name pattern, engine list default, `maxResults` 1–20); implement with validation. Verify the tests pass.

## 2. Search

- [x] 2.1 Write failing `SearxngSearchProvider` tests against an in-process fake server (`TestHost`): one request with `format=json`, the engines, the language and the query; auth header from the secret store; results mapped (title, snippet, URL, domain); 5xx and timeouts give `web.unavailable`. Implement `IWebSearchProvider`. Verify the tests pass.
- [x] 2.2 Write failing tool tests: `web_search` taints with `web`, wraps results, caps the query at 300 characters, returns result ids, never requests a result URL, stores results per session for one hour; tools hidden without configuration; `web_search` executes without confirmation in a tainted run. Add `web` to `ReservedNamespaces`, the namespace description, and implement `WebToolSource` and `ISessionWebResults`. Verify the tests pass.

## 3. Answer and extraction

- [x] 3.1 Write failing `web_answer` tests with `FakeChatClient`: only titles and snippets reach the extractor; typed answers leave the run untainted and return domains; `text` taints and is wrapped; `unknown` passes through. Implement. Verify the tests pass.
- [x] 3.2 Write failing resolver tests: `agent_extract` with `web:<id>` resolves the current session's result (title and snippet only); another session's or an expired id fails with `extract.not_found`. Implement `WebContentResolver`. Verify the tests pass.

## 4. Demo, docs and notes

- [x] 4.1 Write a failing demo test, then add a simulated search provider to `Marshal.Demo` (fixed results, no network) used when demo mode is on and `web` is not configured. Verify the demo tests pass and no network host is contacted.
- [x] 4.2 Write the web search guide (SearXNG setup with JSON enabled, LAN-only, engine choice, privacy statement about queries), add `web` to the configuration reference and a commented example to `deploy/config.example/marshal.yaml`. Verify the example config loads in the config load test and the page follows `docs/development/docs-style.md`.
- [x] 4.3 Add the search exception to `docs/internal/brainstorm-notes.md` §4 with a pointer to the `web-search` spec. Verify the note is present.

## 5. Integration check

- [x] 5.1 Run `dotnet test` and `openspec validate web-search --strict`. Verify both pass.
