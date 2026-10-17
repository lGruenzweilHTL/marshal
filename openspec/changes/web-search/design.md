# Design

## Context

See proposal.md (Why). Current state after earlier changes:

- `quarantined-extraction` provides `IQuarantinedExtractor`, `IUntrustedContentResolver` and `agent.extract`.
- `tool-retrieval-and-latency` loads namespaces per session by description; `web` gets a description.
- The design notes (§4) say every tool that sends data outward is confirm tier, and that no tool fetches a caller-chosen URL. CLAUDE.md repeats the URL rule only.
- SearXNG's JSON API: `GET /search?q=…&format=json&engines=…&language=…` (JSON must be enabled in `settings.yml` with `formats: [html, json]`). Results carry `title`, `content` (snippet), `url` and the engines.

## Goals / Non-Goals

**Goals:**
- Useful lookups without fetching attacker-chosen pages.
- Typed facts without taint.

**Non-Goals:**
- Ranking or re-ranking results.

## Decisions

### 1. Own integration project behind a small interface

`src/Marshal.Integrations.Web` with `IWebSearchProvider` (`Marshal.Abstractions`): `SearchAsync(query, language, limit) → Result<IReadOnlyList<WebResult>>`. v1: `SearxngSearchProvider` (named `HttpClient` with resilience, optional auth header from the secret store per request). `WebToolSource` provides the tools; `WebContentResolver` (`Kind = "web"`) resolves `web:<id>` from `ISessionWebResults`, an in-memory per-session store with a one-hour expiry (no table; results are not worth persisting).

| Tool | Tier | Hidden when |
|---|---|---|
| `web.search(query, limit?)` | safe, `untrustedOutput` (taint `web`) | no `web` section |
| `web.answer(question, answerType, values?)` | safe; taints only for `text` answers | no `web` section |

### 2. Why queries stay safe

Exfiltration needs an observer the attacker controls. With a fixed engine list, queries reach Google, Bing, DuckDuckGo and Wikipedia, not attacker hosts, and Marshal never follows result URLs. What remains is privacy: query text reaches those engines, in tainted and untainted runs alike. A confirm prompt would not change that, only add friction. The decision is recorded here and in the design notes; the engine list is configuration, and the guide warns against adding small or self-hosted third-party engines.

Alternative: confirm tier in tainted runs. Rejected by the user after weighing the above.

### 3. `web.answer` flow

Search with the question as query → build content from `title + snippet` of each result, numbered → `IQuarantinedExtractor.ExtractAsync(content, question, type)` → result `{ answer, domains }`. Domains come from SearXNG's URLs and are shown so the user can judge the source. For `text` answers the whole result is wrapped as untrusted data.

## Risks / Trade-offs

- [SearXNG exposed to the internet becomes an open proxy] → the guide says to keep it on the LAN or VPN; Marshal supports an auth header for reverse-proxy protection.
- [Snippets are thin; answers often `unknown`] → acceptable; the model can fall back to `web.search` (tainting) when it needs more.
- [Upstream engines rate-limit SearXNG] → Marshal reports "unavailable" and does not retry beyond the resilience policy.

## Migration Plan

None; the feature is off until `web.searxng` is configured.
