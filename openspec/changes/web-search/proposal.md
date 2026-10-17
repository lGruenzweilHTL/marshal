# Proposal

## Why

Marshal cannot answer questions about the world outside the home ("when does the Christmas market open?", "who won yesterday?"). A self-hosted SearXNG instance gives a private search API (JSON, no API key, no query logs on the user's side) that aggregates the large engines. Search results are written by strangers and can carry injected instructions, so they must be handled as untrusted. Typed facts can come through the quarantined extractor without tainting the run.

## What Changes

- New configuration `web.searxng` (URL, optional auth header secret, engine list, result limit).
- `web.search(query, limit?)` (safe, untrusted output, taint source `web`): returns title, snippet, URL, domain and a result id per hit. It never fetches result pages.
- `web.answer(question, answerType, values?)` (safe): searches, gives only the snippets to the quarantined extractor, and returns a typed answer plus the domains used. Typed answers do not taint; `text` answers taint with source `web`.
- `agent.extract` accepts `web:<result id>` for results of the current session (snippet content only).
- The query is safe in every run, tainted or not. This is an explicit exception to the design notes' rule that every outward send is confirm tier: queries go to the configured large search engines, not to a host an attacker controls, so they are a privacy cost (the query text reaches those engines), not an exfiltration channel. The guide states the privacy cost.
- `web` becomes a reserved namespace.

## Capabilities

### New Capabilities

- `web-search`: configuration, search and answer tools, result handling, the outward-query decision.

### Modified Capabilities

- `tool-registry`: `web` is reserved.
- `quarantined-extraction`: `web:` references.

## Non-goals

- Fetching or reading result pages (no caller-chosen URL is ever fetched).
- Browser use.
- Other search providers (Brave, Kagi, Bing API); the interface allows them later.
- Caching results across sessions.

## Impact

- New project `src/Marshal.Integrations.Web` (SearXNG client, tools, content resolver) with its test project; repository map in `CLAUDE.md`; `packages.puml`.
- `src/Marshal.Core`: reserved namespace; extractor reuse.
- Docs: web search guide (SearXNG setup with JSON format enabled, not exposed publicly, engine selection, privacy).
- `docs/internal/brainstorm-notes.md` §4: note the search exception.
- `Marshal.Demo`: simulated search results when SearXNG is not configured in demo mode.
