# Marshal — Brainstorm Notes

Working scratchpad. Not a spec. Will be formalized into OpenSpec once the discussion is complete (on explicit go only).

Status legend: **DECIDED** · **LEANING** (proposed, fine for now, may change) · **DEFERRED** · **OPEN**

Contents:
1. Vision, scope & principles
2. LLM backend & model usage
3. Agent harness
4. Risk tiers & confirmation guard
5. Language
6. Home Assistant integration (inbound contract & sessions)
7. Home Assistant MCP usage
8. Per-satellite context
9. Memory
10. Background & independent work
11. Notifications
12. Daily briefing
13. Moonraker (3D printer)
14. Mail
15. FreshRSS
16. Calendar
17. Notes
18. Web UI
19. Auth & users
20. Deployment & config
21. Observability, retention, backups
22. Testing

---

## 1. Vision, scope & principles

Marshal: self-hosted ASP.NET conversation agent that Home Assistant conversation entities talk to over REST. Single user per instance, runs on the LAN next to HA (Docker). Voice via HA Assist is the main interface, so replies must be short and speakable. Other people should be able to run their own instance.

Capabilities:
- 3D printer control via Moonraker (incl. camera snapshots, failure detection) — §13
- Mail (Gmail, Exchange, generic IMAP): read + create drafts. **Never send.** — §14
- Layered semantic memory (pinned + retrieved + per-project) with memory writes from conversation — §9
- Extensibility: custom skills, MCP servers, agent templates — §3
- Home Assistant MCP client — §7
- FreshRSS access + daily briefing — §12, §15
- Background tasks / independent work, incl. delegated coding sessions — §10
- Proactive push notifications — §11
- Per-satellite context — §8
- Calendar (via HA in v1) — §16
- Notes access + proposal system — §17
- Web UI — §18

### Guiding principles — DECIDED
- **Small interfaces everywhere.** Every integration and subsystem sits behind a small interface for extensibility, even when v1 ships exactly one implementation (mail providers, calendar, executors, identity resolver, auth providers, notification channels, failure detector, ...). Applies to every future decision.
- **Tool visibility.** Anything the model cannot do, it does not see. Tools are filtered out of the tool list (not refused afterwards) when they are forbidden, not in the template allowlist, denied for the satellite, or confirm-tier on a satellite that cannot confirm them. The guard still enforces server-side as a second line.
- **Briefings (and similar features) are only tasks.** No feature-specific code where the generic task system suffices.

### Sub-project decomposition — DECIDED
Each sub-project gets its own spec → plan → implementation cycle. First spec sketches the full architecture.
1. Core: agent loop, LLM providers, conversation endpoint, sessions, tool abstraction, guard, config, secret store, machine tokens. In-conversation confirmation only. The latency cap here only streams `status` events and ends the turn with a short "this takes too long" reply; there is no backgrounding yet. Machine tokens are created with an admin CLI command in the container (`marshal tokens create --name ha --scope conversation:write`), because the token UI only arrives in sub-project 2; the CLI stays as the headless path afterwards.
2. Human auth + web shell: login providers (OIDC, local), API token management, secrets / accounts pages, text chat. Comes early because later sub-projects need a logged-in browser (OAuth connect flows, approvals).
3. HA integration (Python custom component, §6): only consumes the public conversation API, so Core is built and tested without it. Marshal only ever receives text (HA runs STT before forwarding), so no voice testing is needed on the Marshal side.
4. Extensibility: MCP client (HA MCP first), skills, agent templates
5. Memory
6. Integrations: Moonraker, mail, FreshRSS, calendar, notes — tools and read paths only. The Moonraker websocket client and state cache are built here (status tools read the cache, §13); nothing is published to an event bus yet.
7. Background work: scheduler, tasks, events, executors, notifications. Also adds everything in the conversation path that needs a durable task: auto-background at the latency cap, the `agent.move_to_background` tool, the approval queue and voice follow-ups (§6, §10). Until then these tools do not exist, so the visibility rule (§1) hides them automatically. Everything that needs the EventBus or the NotificationRouter lands here too, even when it belongs to an integration: Moonraker semantic events and the print babysitter (§13), new-mail events (§14), HA state-change events for conditional delivery.

Each sub-project from 4 on adds its own web UI views (§18) instead of one big web UI phase at the end.

---

## 2. LLM backend & model usage — DECIDED
Provider-neutral via `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`). Both local and Claude API. Providers: Ollama (`OllamaSharp`), any OpenAI-compatible server such as llama.cpp `llama-server`, vLLM or LM Studio (`Microsoft.Extensions.AI.OpenAI`), Claude (official `Anthropic` SDK). Provider packages are referenced only by the host.

**Tiers** mapped in config:
- `fast` — voice turns, judge, cheap classification
- `standard` — normal work; subagents for easy tasks use weaker models
- `advisor` — strong model, only consulted for planning advice when actually needed

**Capabilities instead of more tiers:** each configured model has flags `vision`, `tools`, `contextLength`. A call requests tier + required capabilities; the router picks the tier's model that has them (e.g. `fast`+`vision` = local Qwen2.5-VL / Gemma 3 via Ollama; `standard`+`vision` = Claude). No separate `vision` tier.

**Advisor limits:**
- No tools. Gets the question + an agent-written context summary (no full transcript). Returns advice text only.
- Max 2 advisor calls per run (configurable per template).
- **Hidden in voice turns** (`inputMode = voice`, §6; visibility rule). Visible in interactive text turns (web chat, typed Assist, API) and in background runs. Voice turns needing planning move to the background — either the model calls `agent.move_to_background` (§6) or the latency cap fires. The tool list is recomputed for background rules on that switch, so the advisor becomes visible there.
- Advisor cost counts against the task budget + separate daily advisor cap; own line in the cost view.
- Prompt nudge: use only for multi-step planning or when stuck after a failed attempt. Hard limits are the real control.

---

## 3. Agent harness — DECIDED
Own thin harness on primitives, not a full framework (rejected: Microsoft Agent Framework — generic model fights domain concepts, churn; Claude Agent SDK — TS/Python + Claude-only).
Built on `Microsoft.Extensions.AI` + official `ModelContextProtocol` C# SDK.

Parts:
- **Agent loop:** model → tool calls → guard check → execute → repeat, max steps
- **Tool registry:** built-in C# tools, MCP tools, skill tools; each with risk rules (§4); visibility filtering (§1)
- **Context builder:** system prompt + template + pinned memory + retrieved memory + satellite context + skill index (names/descriptions only)
- **Model router:** tiers + capabilities → providers (§2)
- **Session store:** see §6
- **Task runner:** same loop, headless (§10)
- **Subagents:** `agent.delegate(template, task)` tool → nested Marshal agent loop (same harness, same guard, template's tools and model tier, usually cheaper), result returned inline to the calling loop. **Subagents never run confirm-tier actions:** confirm-tier tools are hidden in the nested loop; arg-aware tools stay visible, but the guard denies every call that resolves to confirm. A subagent has no user to ask; if it needs a confirm-tier action, it says so in its result and the calling loop performs the action itself (with the normal confirmation or approval flow). Subagents inherit the caller's satellite policy and taint (§4). Not to be confused with executors (§10: `tasks.run_executor`), which hand long external work to another system and report back later as an event.
- **Advisor:** `agent.ask_advisor(question)` tool → single strong-model call with trimmed context (§2)

Formats (file-based, git-friendly):
- **Skills:** Anthropic Agent Skills format (`SKILL.md` + frontmatter + files). Progressive disclosure via `agent.load_skill`.
- **Agent templates:** Markdown + frontmatter (like Claude Code subagents): `name`, `description`, `modelTier`, `tools` allowlist, `skills`, prompt body.
- **MCP servers:** config list (stdio + streamable HTTP), with risk rules (§4). A server whose tools return third-party content (web fetch, search, ...) is marked `untrusted: true`; its results taint the run (§4). MCP tools that no risk rule matches default to `confirm` (fail closed); built-in tools use their own default tier.

### Tool naming — DECIDED
Model APIs only accept tool names matching `^[a-zA-Z0-9_-]{1,64}$` (no dots). So every tool has two names:
- **Internal id** `<namespace>.<name>`, used everywhere in Marshal: risk rules, satellite `denyTools` / `denyConfirm`, template `tools` allowlists, run records, audit log. Patterns are globs on the id (`mail.*`, `ha.Hass*`).
- **Model-facing name** `<namespace>_<name>`, derived from the id; the tool registry keeps the mapping in both directions.

Namespaces:
- **Built-in tools use capability namespaces**, never provider names (small interfaces, §1): `agent` (`agent.move_to_background`, `agent.delegate`, `agent.ask_advisor`, `agent.load_skill`), `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`. These are **reserved**.
- **MCP tools use flat namespaces:** the namespace is the server's `id` from config (`ha.HassTurnOn` → model name `ha_HassTurnOn`). No `mcp.` prefix (saves tokens on every turn).
- Optional per-server `toolPrefix` in config overrides the namespace (e.g. a server with a long id). Default: the server id.

Rules:
- Namespaces match `[a-z][a-z0-9]*` (no underscore, no dash), so the first `_` in a model-facing name always separates namespace and name. Server ids / `toolPrefix` that do not match are a config error.
- Built-in tool names are `snake_case`. MCP tool names are kept as the server reports them; characters outside `[a-zA-Z0-9_-]` become `_`, and a name longer than 64 characters is truncated with a short hash suffix.
- **Collisions** (MCP namespace equal to a reserved built-in namespace or another server's, or two tools mapping to the same model-facing name) → that MCP server is rejected at startup / reload with a clear error; the rest keeps working.

---

## 4. Risk tiers & confirmation guard — DECIDED
**Tiers:** `safe` / `confirm` / `forbidden`. Forbidden capabilities (e.g. mail send) simply don't exist as tools.

**Arg-aware risk rules** (generic risk-engine feature; used for HA, Moonraker macros, any MCP server):
```yaml
risk:
  - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
    tier: confirm
  - match: { tool: "ha.*", domain: cover, deviceClass: [garage, door, gate] }
    tier: confirm
  - match: { tool: "ha.*" }
    tier: safe
```
- Rules match tool ids (globs, §3 tool naming), the same way for built-in and MCP tools; first match wins.
- Tools whose tier depends on args stay visible; the guard decides per call.
- **Target resolution expands every filter the call carries**, not only names: HA intents target by `name`, `area`, `floor`, `domain` and `device_class`, so `HassTurnOff(area: "hall")` resolves to every exposed entity in the hall. Rules are evaluated per resolved entity and the **highest tier wins** (one lock in the set → the whole call is confirm). This matters because HA's generic on / off intents also act on locks and covers (e.g. turn off a lock = unlock — verify the exact domain mapping when building).
- Args that cannot be resolved to a known target (e.g. unknown HA entity name), or that resolve to an empty set → confirm (fail closed).
- **Tier escalation by taint:** see "Untrusted content" below. The guard may raise a call's tier, never lower it.

**Flow for `confirm`-tier calls:**
1. Server freezes the pending action: tool, args, session, satellite, nonce, TTL (~60 s), and **the reason confirmation is needed**. Agent asks the user for confirmation (`continueConversation` = true).
   - **Every confirmation explains itself.** The guard, not the model, determines the reason, as a code plus a short localized sentence (request language, §5): `rule` (the risk rule makes this tool / target confirm tier, e.g. "this unlocks the front door"), `unresolved_target` ("I couldn't tell exactly which device you mean"), `taint` (see below), `quiet_hours_override`.
   - The reason is part of the frozen action. The tool result that tells the model "confirmation pending" carries the reason sentence, and the system prompt requires the question to include it. Channels the model does not write (approval queue in the web UI, actionable push) show the server sentence verbatim, so the explanation is there even if the model drops it.
   - Several reasons can apply; all are stored, the most specific one is spoken (`taint` first, since it is the one a user would not expect).
2. Next user turn:
   - **Exact match** (after normalization: trim, lowercase, strip punctuation; full utterance only) against configured keywords per language (§5) → execute frozen action. Bypasses the model; server-validated only.
   - Otherwise → **small judge LLM** sees only action summary + user reply, temperature 0, structured enum output:
     - `positive` → execute
     - `neutral` (likely topic change) → drop pending action, process message as a normal new turn
     - `negative` → drop, reply "cancelled"
   - The judge's system prompt tells it to answer `neutral` whenever it is not confident. There is no separate confidence score.
   - Judge timeout / parse error → **fail closed** (drop).
3. Executed action is always the frozen one — model cannot alter args after confirmation.

**Rules:**
- Only user actions can confirm: a user turn, a web UI approval, or a nonce-verified push action. Tool output (mail bodies, RSS, notes) never counts → blocks prompt injection.
- One pending action per conversation; a new one supersedes.
- Background tasks: no user present → confirm-tier actions go to the **approval queue** (actionable push or web UI), same freeze + nonce model, but with a **10-minute TTL** (in-conversation confirmations keep ~60 s). An expired approval counts as rejected.
  - **The run suspends, it does not block a worker.** The frozen action is queued, the server appends an automatic entry to the task's `notes` ("approval requested: `<tool>` with `<args summary>`"), and the run ends with `status=waiting` and trigger = the approval decision.
  - On approve, the **server** executes the frozen action (the model never re-issues it). The task then resumes like any wakeup (§10): goal + notes + trigger payload, where the payload carries the decision and, when approved, the action's result. Rejected / expired → the payload says so, and the task continues or fails from there.
- Per-satellite policy (`denyConfirm`, `denyTools`) — §8. Arg-aware tools on a `denyConfirm` satellite stay visible; the guard denies calls that resolve to confirm with a clear tool error ("not allowed from this device"). A tool whose every matching rule is confirm is hidden.
- Any future tool that sends data outward is confirm tier. This includes tools that fetch a caller-chosen URL: a GET with data in the query string is an outward send. Fetch tools take ids of known items instead (e.g. `rss.fetch_full_text(articleId)`, §15).
- **Exception: web search queries are safe**, also in tainted runs. They go to the user's SearXNG and from there only to the configured engine list (Google, Bing, DuckDuckGo, Wikipedia by default), not to a host an attacker controls, and result URLs are never followed. What remains is privacy (query text reaches those engines), which a confirm prompt would not fix. Decided in the `web-search` change; see `openspec/specs/web-search/spec.md`.

**Untrusted content and persistence (taint):**
- A run becomes **tainted** as soon as untrusted tool output enters its context: mail, RSS articles, notes, calendar events (invitations come from third parties), notification inbox items, web pages, and MCP tools marked `untrusted` in their server config. Taint is per run and never cleared within it; subagents inherit it and pass it back.
- Tools that **persist instructions or state the agent will act on later** carry a `persists` flag: `memory.remember`, `tasks.create`, `tasks.watch`, `tasks.schedule_wakeup`, `tasks.update`. In a tainted run the guard escalates them from safe to **confirm** (in background runs → approval queue). This blocks an injected mail from planting a memory or a watch that fires later.
- **Taint confirmations name the source.** A simple reminder asking for confirmation looks odd without a reason, so the reason sentence says what caused it and why, e.g. "This conversation included text from your mail, and mail can contain hidden instructions. Should I really create the reminder 'reply to Anna' for tomorrow at 9?" The source kinds (mail, RSS, notes, web, named MCP server) are tracked per run with the taint flag, so the sentence can name them. The web UI approval view shows the same text plus a link to the tool result that tainted the run.
- Drafts and notes proposals are not escalated: both are reviewed by the user before they have any effect.
- **Pinning memory is UI-only.** `memory.remember` cannot pin; pinned chunks are always injected in full, so only the user decides what is pinned.
- Nightly consolidation reads user turns and assistant replies only, never raw tool results (which are stored as references for sensitive tools anyway, §21).

---

## 5. Language — DECIDED: German + English
Models and good STT handle both. What actually changes:
- **HA pipeline:** each Assist pipeline has a fixed language (STT, local intents, TTS voice). Options: two pipelines (e.g. one wake word per language), or STT auto-detect where supported. HA-side config, not Marshal.
- **Reply language = request language:** HA sends `language` per turn; Marshal replies in it so the (often monolingual, e.g. Piper) TTS voice matches. Runs without a request (tasks, notifications, briefing) use the user's stored language preference (§19).
- **Confirmation keywords:** one list per language (e.g. "confirm" / "approved", "bestätigt" / "bestätigen"). Judge LLM is multilingual.
- **Embeddings must be multilingual** (e.g. `bge-m3`, `multilingual-e5`, `nomic-embed-text-v2`), otherwise a German query misses an English chunk. FTS keyword search will not cross languages — accepted, embeddings cover it.
- **Canonical memory language:** chunks written in English regardless of conversation language (stable titles → stable deterministic IDs, better dedup).
- **Fast local model** must be decent in German — factor into model choice.
- Prompts / system text stay English; locale-aware formatting (dates, numbers) from request language.

---

## 6. Home Assistant integration: inbound contract & sessions — DECIDED
- **Inbound:** thin custom HA integration (Python, HACS-installable) registers a conversation agent and forwards each turn to Marshal.
- **Outbound:** Marshal calls HA REST / websocket + HA MCP server with a long-lived token (§7).
- Optional later: OpenAI-compatible endpoint as compatibility bonus.

**Endpoint:** `POST /api/v1/conversation/turns`, policy `conversation:write`. One endpoint for all interactive clients:
- **Token clients** (HA integration, API): machine token with scope `conversation:write`. `conversationId` is required; the session is looked up by `(api_token_id, conversationId)`.
- **Cookie clients** (web chat): the logged-in user. Cookie principals carry the scopes their role grants (`user` → all non-admin scopes, `admin` → all), so both go through the same policies (§19). `conversationId` is optional: omitted = new session; otherwise it is Marshal's own session id and must belong to the user. The final event returns Marshal's session id.

Request:
```json
{
  "conversationId": "01J...",
  "text": "wie weit ist der Druck?",
  "language": "de",
  "deviceId": "...",
  "satelliteId": "assist_satellite.kitchen",
  "areaId": "kitchen",
  "haUserId": null,
  "inputMode": "voice",
  "followupRef": null
}
```
- **JSON is camelCase** across the whole public API (ASP.NET default, matches the generated TypeScript client). The Python HA component maps to HA's snake_case names at its edge.
- `followupRef`: opaque reference Marshal handed out with a voice follow-up (§10); the component forwards it when the reply turn carries one.
- `inputMode` (`voice` | `text`) is set by the HA integration per turn: `voice` when the turn came through STT (satellite or app microphone), `text` when typed (HA app / Assist dialog). Web chat and API clients always send `text`. It drives the output mode (§8). How the integration detects it (e.g. pipeline STT stage vs. no STT) → verify when building the component.
- **Terms used throughout:** a **voice turn** is a turn with `inputMode = voice`; an **interactive turn** is any turn with a user waiting for the reply (voice or text, any source); a **background run** is a task run. Rules keyed to "voice" (advisor hidden, speakable output, the ~8 s latency cap) follow `inputMode`, not `source`.

Response: streamed as **Server-Sent Events** (`text/event-stream`, .NET `TypedResults.ServerSentEvents`):
- `delta` events (text chunks)
- `status` events (optional, e.g. "checking printer")
- final event: `{ speech, continueConversation, conversationId }`. `conversationId` echoes the client's id (HA `conversation_id`, stored as `external_id`), never Marshal's internal session id.

Rules:
- **Streaming from day one** (HA can stream LLM output into TTS; verify exact HA version / API when building the component). Non-streaming = same stream collected.
- **`continueConversation`** (HA: `continue_conversation`) true when Marshal asks something (confirmations, clarifications) → satellite keeps listening without wake word.
- **Latency budget + auto-background:** target first speech < ~2 s for simple voice turns; hard cap ~8 s for voice turns and ~60 s for text turns (both configurable). At the cap Marshal says "working on it, I'll tell you when done"; the run continues as a background task; result delivered via notifications (§11).
- **Self-backgrounding:** the model can call `agent.move_to_background(status_message)` (safe, visible only in interactive turns) when it sees early that the work is long or needs planning. Same effect as the latency cap: the status message is spoken, the run continues as a background task.
- **Any switch to background recomputes the tool list** with background rules: advisor visible, confirm-tier actions go to the approval queue instead of an in-conversation confirmation. The originating satellite's `denyTools` / `denyConfirm` policy (§8) still applies — backgrounding never widens what a satellite may do. To make that hold, the task stores the originating satellite (`origin_satellite_id`) and every run of it, and of every child task it creates, applies that satellite's policy. Tasks created from web chat or the API carry no satellite and get no satellite policy.
- **HA state access** only via HA MCP / REST. The integration sends no entity dumps.
- **Everything goes through Marshal:** the Assist pipeline does **not** use "prefer handling commands locally". Every turn reaches Marshal; Marshal performs home control and timers through HA intents (HA MCP, §7). Trade-off accepted: simple commands pay one model round trip (use the `fast` tier).

### Sessions and ownership
```
Session
  id                  Marshal-internal id
  source              voice | web | task | api   (which kind of client; voice = HA Assist integration, also for typed Assist turns)
  api_token_id        API token that created it (voice, api); null for web (cookie) and task sessions
  external_id         client-supplied conversation id (HA conversation_id); null when Marshal generates the id
  owner_id            resolved on first turn, then pinned
  identity_evidence   { ha_user_id, device_id, satellite_id, area_id }
  resolved_by         owner_default | ...
```
- Token clients: lookup by `(api_token_id, external_id)`, never by the client-supplied `conversation_id` alone (prevents hijacking by guessed IDs). Web / task sessions are looked up by Marshal's own session id plus owner.
- Per-turn data (`inputMode`, satellite, area, language) lives on the run (§21), not on the session.
- **One user per session.** The owner is resolved on the first turn and pinned; it is never re-resolved. A different user can only appear in a new session (e.g. after the idle TTL closed the old one).
- New session → `IIdentityResolver(ha_user_id, device_id, satellite)` → owner, stored with evidence.
- Everything created in a session inherits `owner_id` (pending confirmations, tasks, memory writes, approvals).
- **v1: the resolver always returns the instance owner** (the only user). The resolution order and fallback policy for multi-user instances are decided when multi-user is built; the seam and the stored evidence exist so that decision needs no schema change.
- Evidence stored now so sessions / memories can be re-attributed when multi-user arrives.
- Web chat: owner = cookie user, Marshal generates the session id. Tasks: owner = task owner. Every API token has an owner (HA integration token → instance owner now, later possibly a "household" principal that must go through the resolver).
- Idle TTL a few minutes → session summarized into memory candidates and closed.

### API versioning — DECIDED
Applies to the whole public API, not only the conversation endpoint.
- **Version lives in code, never in config.** A version describes the contract the code implements; configuring it would make the URL lie and break clients.
- **`Asp.Versioning.Http`** with URL-segment versioning. Versions declared once (`ApiVersions.V1`); endpoints register on a versioned route group `/api/v{version:apiVersion}` and never contain a version literal.
  ```csharp
  var api = app.NewVersionedApi();
  var v1 = api.MapGroup("/api/v{version:apiVersion}").HasApiVersion(ApiVersions.V1);
  v1.MapConversationEndpoints();
  ```
- **Breaking change:** add `V2`, map changed endpoints on a v2 group; v1 keeps working side by side until retired. Deprecation via `api-supported-versions` / `api-deprecated-versions` headers (+ `Sunset` if wanted).
- **One OpenAPI document per version** (`/openapi/v1.json`); the SPA client is generated from the version it targets.
- **Versioned:** everything clients use — conversation turns, tasks, memory, current user (`/api/v1/me`, `/api/v1/me/*`), auth (`/api/v1/auth/*`), live update streams.
- **Not versioned:**
  - `/health` (fixed path for Docker / monitoring)
  - `/api/versions` (discovery must work before a client knows which version to use)
  - `/openapi/{document}.json` (the document name carries the version, e.g. `/openapi/v1.json`)
  - auth protocol callbacks (`/signin-oidc`, `/signout-callback-oidc`): the IdP registration pins these URLs
  - SPA routes
- **Version discovery:** `GET /api/versions` (unversioned) + response headers. The HA integration checks support at setup and shows a clear error in HA if its version is unsupported.
- **Configurable instead:** the base path (§20), e.g. `/marshal` behind a reverse proxy → `/marshal/api/v1/...`.

---

## 7. Home Assistant MCP usage — DECIDED
- HA's MCP Server integration exposes the Assist API as MCP tools (intents like `HassTurnOn`, `HassTurnOff`, `HassLightSet`, `HassClimateSetTemperature`, plus `GetLiveContext` = state of all exposed entities). Streamable HTTP, long-lived token. Only entities exposed to Assist are visible (HA exposure settings = first filter). Tool names / endpoint vary by HA version → verify when building.
- **Risk:** arg-aware rules (§4). Entity resolution via a cached entity registry.
- **Context cost:** `GetLiveContext` cached with short TTL; context builder includes only current-area entities + entities mentioned in the query; model calls the full tool only when needed. HA websocket (event source, §10) keeps the cache fresh.
- **Division of labor:**
  - Model-driven home control and timers → HA MCP intents (`HassTurnOn`, `HassStartTimer`, ...).
  - Marshal system calls (announce, push, presence, calendar v1 implementation) → HA REST / websocket directly, never through HA MCP. (Calendar tools themselves are model-facing, §16; only their implementation uses HA REST.)
- **Timers — verify when building:** HA timer intents are bound to a device that supports timers (the satellite). HA MCP calls carry no device context, so timer intents may not be offered there. Fallback: call HA's intent handling with the turn's `device_id` as a system call behind a Marshal `timers.start` tool. Last resort: timers as Marshal tasks (wakeup + announce), losing the satellite's native timer UI.

---

## 8. Per-satellite context — DECIDED
**Per turn, from where it was spoken:**
- **Location:** area + floor from `area_id` (names from HA). "Turn on the light" / "how warm is it here" resolve to this room; HA MCP tools take area arguments.
- **Output mode** derived from the turn's `inputMode` / source (§6), not configured per device: voice → plain text, no markdown, short, numbers spoken naturally; text in web chat → markdown, links, images; typed Assist turns → whatever the HA Assist dialog renders (verify markdown / image support when building the component; fall back to plain text with links).
- **Time & locale:** local time, day of week, request language.
- **Optional satellite profile** in `/config/satellites.yaml` (hot-reloaded); unconfigured satellites use defaults:
  ```yaml
  satellites:
    assist_satellite.kitchen:
      verbosity: short
      notes: "Cooking area. Timer and recipe questions are common."
    assist_satellite.kids_room:
      verbosity: short
      denyConfirm: [printer.*, calendar.*]      # confirm-tier tools hidden here
      denyTools: [mail.*]                       # hidden entirely
  ```

**Implementation:** small `ISatelliteContextProvider(request) → SatelliteContext`. Context builder renders a few lines into the system prompt. Tool filtering applies `denyTools` / `denyConfirm` (hidden, §1; arg-aware tools: §4); guard enforces the same policy server-side.

**Satellite policy is a convenience, not an authorization boundary.** It limits what a fixed device in a room can do, but anyone who can reach the same user through another client (HA app on a tablet: no `satelliteId`, defaults apply; web UI) bypasses it. Real restrictions for other people come with multi-user (separate users and roles).

**Not now:** who else is in the room (comes with multi-user); area-specific memory (generic memory covers it, area is part of the turn embedding input).

---

## 9. Memory — DECIDED
Embedding-based semantic memory, inspired by granum (github.com/e-reitbauer/granum), ported to .NET.

Why embeddings: the corpus will outgrow the context over months. Known failure modes and mitigations:
- Silent misses → hybrid search (embeddings + FTS keyword) + explicit `memory.recall` tool for deliberate deep search
- Stale / contradictory facts → `supersedes` edges, freshness decay, periodic audit
- Similarity ≠ identity → deterministic IDs

Taken from granum:
- Typed chunks: `fact`, `preference`, `event`, `procedure`, `person`, `device`
- Deterministic chunk ID = hash(owner_id + scope + project_id + type + normalized title); never similarity-merge. `owner_id` keeps two users' chunks apart (multi-user prep); `scope` + `project_id` keep same-titled chunks in different projects apart (`project_id` is empty for `general`). The project's stable id is used, not its name, so renaming a project never changes chunk ids. Moving a chunk to another scope or project = new chunk + `supersedes` edge.
- Edges: `supersedes`, `contradicts`, `depends_on`, `relates_to`; two-phase retrieval (cosine seeds → BFS expansion)
- Score = similarity × importance × freshness; per-type quotas
- Soft deletes; version history in two forms:
  - **Same identity, new content** (same owner / scope / project / type / normalized title → same id): the chunk is updated in place and the previous content is kept as a `ChunkVersion` row (content, importance, source, timestamp). This is the normal "fact changed" case, e.g. an updated device setting.
  - **Different identity** (title changed, scope or project moved, a contradiction resolved in favor of another chunk): a new chunk plus a `supersedes` edge from new to old; the old one is soft-deleted or kept as history. Supersedes edges never point from a chunk to itself.
- Audit: stale / conflicts / orphans
- NDJSON export to git for portability
- Visualizations (force graph, PCA scatter) → web UI memory view (§18)

Changed for Marshal:
- **Automatic retrieval** server-side in the context builder (no model-invoked `query_context` → saves a round trip per voice turn)
- Scopes: `general` or `project` (with a `project_id`); no git-branch scoping
- Storage: SQLite (FTS5 + `sqlite-vec`), edges as a table; embeddings via `IEmbeddingGenerator` backed by local Ollama with a **multilingual** model (§5)
- Canonical chunk language: English (§5)
- Not reusing granum directly (Python, Claude-Code-coupled, Kuzu — reportedly archived late 2025, verify)

Layers:
- **Pinned** (always injected in full, kept small) — replaces a separate "general" file. Pinning and unpinning happen only in the web UI (§4, §18).
- **General retrieved** — scope `general`, relevance-based
- **Project** — scope `project` + `project_id`, boosted when project active
- **Notes are not part of memory** (§17). They are a separate information source reached through notes tools. When a note contains something worth remembering, the agent writes a normal memory chunk in its own words, with `source_ref` pointing to the note path.

Writes: `memory.remember` tool (cannot pin; escalated to confirm in tainted runs, §4) + background consolidation (cheap model reviews finished conversations, proposes chunks, dedupes by ID, flags contradictions). Nightly consolidation is a cron task.

### Project activation — DECIDED
- **Explicit mention loads** (project name or known alias).
- **Semantics suggest** — applied silently, never asked by voice (loading is read-only; false positive = a few tokens, false negative = missing context).
- No satellite ↔ project binding.

Mechanisms (daily use will be mostly inferred — project names are rarely spoken, e.g. "the voice assistant" for Marshal):
1. **Learned aliases:** triggering phrases become alias candidates when inference isn't corrected; promoted after 2–3 confirmations. Alias match = explicit (deterministic, no model call).
2. **Soft scoping:** retrieval always searches all scopes; active projects boosted, inactive penalized (never hard-gated).
3. **Project centroid matching:** project description + top chunks → centroid vector; compared against the turn embedding already computed for retrieval (no extra call).
4. **Confidence tiers:** high → sticky activation; medium → this turn only; low → nothing.
5. **Stickiness with decay** across turns; topic shift drops it.
6. **Recency prior** for recently touched projects.
7. **Cheap correction:** "not that project" / "that's about X" → unload / switch, negative example, alias learning. Every correction and confirmation is stored as a `ProjectFeedback` row (project, kind `confirmed | rejected | switched`, triggering phrase + its embedding, run). Rejected phrases act as negative examples in centroid matching; confirmations count toward alias promotion. The rows are also the correction log in the web UI (§18).
8. **Transparency** in web UI / logs (active projects per turn and why) — not in voice.

---

## 10. Background & independent work — DECIDED
The agent largely controls this itself (e.g. start a coding session and attach a voice update when finished; self-scheduled wakeups; cron jobs). The server enforces limits.

### Task model (durable, SQLite)
`id, owner_id, parent_id, created_by, goal, template, project_id (optional; the project is activated on every run, §9), origin_satellite_id (optional; its policy applies to every run and child task, §6), trigger (now | at | cron | event(filter) | after(task)), status (pending | running | waiting | done | failed | cancelled), next_run_at, notes (scratchpad carried between runs), budget (tokens / cost / steps / wall time / runs), deliver (list of deliveries, each: channel voice(satellite | area | "where I am") | push | inbox | silent, priority `normal` | `low` (default `normal`), optional `followUp` (voice only: listen for a reply), optional `when` state condition, optional `expires` deadline + fallback channel), override_quiet_hours_until (confirm tier, §11), name (optional, unique per owner, e.g. `briefing`), result { speech, body }`

- **Wakeup** = run ends with `status=waiting` + new trigger. Resume gets goal + notes + trigger payload, **not** the old transcript (no context rot).
- **Result = `speech` + `body`.** `speech` is short and speakable, `body` is markdown (links, tables, details). Voice channels use `speech`; push uses `speech` plus a deep link to `body`; the web UI always shows `body`.
- **Several deliveries per task.** `deliver` is a list; each entry is delivered independently (e.g. voice when the kitchen is occupied, plus inbox).
- **Delivery validation:** `priority: low` is never voice (§11), so a delivery with `channel: voice` and `priority: low` is rejected when the task is created or updated (validation error to the model / UI), not silently changed.
- **Time zone:** cron expressions, `at` times, `expires` times and quiet hours are evaluated in the owner's time zone (§19), with DST handled by Cronos. Stored timestamps stay UTC.
- **Deliveries go through the NotificationRouter (§11)** with the delivery's priority. Task results are `normal` or `low`, never `critical`, so they always respect quiet hours, DND and the voice caps; only `override_quiet_hours_until` (confirm tier) lifts quiet hours for one task.
- **Conditional delivery:** a delivery can wait for its `when`, a **state condition** on Home Assistant entities, not an edge. It is checked immediately when the result is ready (already true → deliver now) and re-checked on every matching state-change event. If `expires` passes first, it goes out via the fallback channel (e.g. push), so results never rot.
  - Syntax: `state(<entity_id> <op> <value>)` with `=`, `!=`, `>`, `<` (numeric); several conditions in a list must all hold.
  - **Room presence:** occupancy / presence `binary_sensor.*` entities (e.g. `state(binary_sensor.kitchen_occupancy = on)`). HA areas are not entities and have no state, so conditions never reference an area directly.
  - **Home presence** (independent of rooms and device areas): the owner's `person.*` entity (`state($person = home)`; `$person` resolves to the person entity from the owner's notification targets, §19), or `zone.home` whose state is the number of people at home (`state(zone.home > 0)`).
- **Named tasks:** optional `name`, unique per owner, so tools can address a task without knowing its id (`tasks.status("briefing")`).
- **System tasks** (backups, exports): cron tasks of type `system`, no agent, no tokens.

### Components
- **Scheduler:** own `BackgroundService` + `PeriodicTimer` over `next_run_at`; Cronos for cron parsing (Quartz / Hangfire too heavy); `TimeProvider` for testability
- **EventBus:** in-process `Channel<T>`; adapters normalize to `{source, type, entity, payload}`. Sources: HA websocket, Moonraker websocket, mail (IDLE / polling), executor callbacks
- **TriggerMatcher:** declarative filters, deterministic, no LLM until match
- **Worker pool:** N concurrent headless agent loops
- **Executors:** `IExecutor` for long external work (`Start(spec)` → handle, completion arrives as event)
- **Notifications:** §11
- **Voice follow-up:** `assist_satellite.start_conversation` → announce + listen; the answer can feed the confirmation flow, a waiting task, or simply continue talking about a delivered result.
  - HA gives the reply turn a **new conversation id** Marshal has never seen (the action takes no `conversation_id`), so session lookup alone cannot link it.
  - Marshal stores a **follow-up** record (nonce, satellite, pending action or task, short expiry) and passes `marshal-followup:<nonce>` as the action's `extra_system_prompt`. HA hands that to the satellite pipeline's conversation agent; the HA component extracts it and sends it as `followupRef` (§6).
  - On a turn with a valid `followupRef` (known, unexpired, unconsumed, same satellite), Marshal creates the new session and links it to the follow-up's target:
    - **Pending action:** the reply goes into the confirmation flow. Confirmation rules are unchanged: the user's utterance still confirms (keyword or judge); the ref only says which question the reply answers.
    - **Waiting task:** the reply becomes the task's trigger payload and the task resumes.
    - **Delivered result** (delivery with `followUp: true`, e.g. the briefing): the new session is **seeded** with the task's result as an earlier assistant message (the spoken `speech`, with `body` available as context) plus a reference to the task. The reply is then a normal turn in that session, so "tell me more about the second article" works without re-running the task.
  - No reply within the satellite's listening window → nothing happens; the follow-up record expires unconsumed.
  - Verify when building the component that `extra_system_prompt` reaches custom conversation agents. Fallback only if it does not: a short satellite-keyed reply window.

### Agent tools
| Tool | Tier | Notes |
|---|---|---|
| `tasks.create`, `tasks.schedule_wakeup`, `tasks.watch(event_filter, goal)` | safe (`persists`: confirm in tainted runs, §4) | `override_quiet_hours_until` arg → confirm (§11) |
| `tasks.update(task, goal?, trigger?, deliver?, template?)` | safe (`persists`: confirm in tainted runs) | edits a task by id or name (e.g. the self-editable briefing); `override_quiet_hours_until` arg → confirm; cannot change owner, budget or `origin_satellite_id` |
| `tasks.run_now(task)` | safe | runs an existing task immediately (e.g. the briefing on request, §12); same budget, deliveries and conditions as a scheduled run |
| `tasks.list` / `tasks.status` / `tasks.cancel` | safe | `status` and `cancel` accept an id or a name |
| `tasks.stop_all` | safe | the kill switch (below); stopping is always allowed |
| `tasks.run_executor(executor, spec, on_done_goal)` | confirm | starts external long-running work |
| `notifications.notify(message, priority, deliver)` | safe | `priority` is `normal` or `low` only (§11) |
| `notifications.inbox_list` / `notifications.inbox_consume` | safe | read and mark consumed items of the owner's inbox (§11) |
| `agent.move_to_background(status_message)` | safe | interactive turns only (§6) |

### Guardrails (server-enforced)
- Per-task budget + global daily cost cap; children draw from the parent budget
- Recursion depth limit (~3), fan-out limit
- Minimum wakeup interval (e.g. 5 min, per template)
- Confirm-tier → approval queue
- Kill switch (voice via `tasks.stop_all`, web UI button): stop all background tasks
- Full audit log per run

### Coding executor — DECIDED
Generic, abstract, extendable `IExecutor`. First implementation = Claude Code headless (`claude -p`, stream-json) in a Docker sandbox, repos mounted, git worktree per task, network allowlist. Runs as a **separate executor service** (§20), never with Marshal holding the Docker socket. Executors get per-task, single-purpose callback tokens (valid only for that task's callbacks, revoked when the executor session ends). Others plug in later (cloud sessions, Codex, aider, ...).

### Timers & reminders — DECIDED (two paths)
- **Short / basic timers** (cooking etc.): the turn reaches Marshal like every other turn; Marshal starts an HA timer intent (`HassStartTimer` etc.) for the satellite the user spoke to (§7, verify device binding). Timers are bound to the satellite device.
- **Long runs / anything needing an agent wakeup** (reminders with context, check-ins, deferred work): Marshal task system (wakeups, cron, watches).

### Use-case ideas
- Print babysitter (snapshot checks, auto-pause on failure, announce on complete) — a **system component**, not an agent task (§13)
- Coding delegation (executor in worktree → cheap-model diff review → voice notice "the change is ready for review" → merge in the web UI review queue; merge is never a voice or agent action, §17)
- Contextual reminders ("when I get home…" → presence watch)
- Mail triage (event → classify → draft replies → `low` notification into the inbox, picked up by the briefing)
- Deferred check-ins ("ask me tomorrow if…" → wakeup + `start_conversation`, answer to memory)
- Self-editable briefing (§12)
- Nightly memory consolidation
- Research → notes proposal + voice summary

---

## 11. Notifications (NotificationRouter) — DECIDED
**Priorities** (set by the sender):
- `critical` — print error, e-stop triggered, spaghetti detected. Always delivered, ignores quiet hours, voice + push. **Only system-defined sources may send `critical`**; the agent's `notifications.notify` cannot (otherwise the model could bypass quiet hours).
- `normal` — task done, print complete, approval needed. Respects quiet hours.
- `low` — FYI. Never voice (a voice delivery with `low` priority is rejected at task creation, §10). Goes into the owner's **inbox** (or a push digest). The inbox is generic: any task reads it via `notifications.inbox_list` / `notifications.inbox_consume` (§10); the briefing task is simply one consumer.

**Channel choice:** 1) the task delivery's channel and `when` condition (§10) → 2) where the user is (home presence via `person.*` / `zone.home`, room via occupancy `binary_sensor.*`, §10): home → voice in the occupied room (`assist_satellite.announce`, or `start_conversation` if a reply is expected / `followUp: true`); away → push → 3) fallback push. Notifications target a person (HA `person.*`), not a device.

**Channels** (same set everywhere): `voice` (announce, or conversation when a reply is expected), `push`, `inbox`. A task delivery may also be `silent` (no notification; the result is only visible in the web UI). The web UI is not a channel: open browsers receive every notification on the live event stream (§18) regardless of the chosen channel.

**Quiet hours** (user preference, e.g. 22:00–07:00, day-of-week aware, in the owner's time zone, §19):
- `normal` → silent push or held until quiet hours end (configurable)
- `low` → inbox
- `critical` → delivered anyway
- **Quiet-hour override (confirm tier, optional):** the agent may mark a specific task / watch with `override_quiet_hours_until` (e.g. "wake me if the print fails tonight"). Confirmed in the conversation when set up, or via the approval queue for self-created tasks. Frozen to that one task / watch, with an expiry.

**Noise control:**
- Coalescing: several `normal` items within ~2 min → one message.
- Per-source limit: e.g. max 1 voice announcement per task per 30 min (push coalesced, not limited).
- Global voice cap: e.g. max 6 unsolicited voice announcements per hour, beyond → push.
- Voice do-not-disturb: "quiet for 2 hours" → temporary quiet period (`critical` still delivers).
- Optional busy detection: media playing in the room / active call → prefer push.

**Actionable push:** HA companion app notifications with buttons (Approve / Reject) → approval queue; reply arrives as `mobile_app_notification_action` event, Marshal verifies the nonce. Push notifications deep-link into the web UI (mobile views, §18).

**History:** every notification is recorded (content, priority, chosen channel, delivered / coalesced / held / expired, reason) → notification history view (§18).

**Interface:** small `INotificationChannel` (implementations: voice announce, voice conversation, push, inbox) — router picks, channels deliver.

---

## 12. Daily briefing — DECIDED
**Rule: a briefing is only a task. No briefing-specific features or code.**

- **Content** lives only in the goal text. The agent uses the normal tools: calendar, mail, RSS pipeline (§15), printer, weather (HA MCP), `tasks.list` (done / failed / awaiting approval), reminders.
- **Files only:** agent template `briefing.md` (model tier, tool allowlist) + a skill with format guidelines (voice length ~60–90 s, most important points first; long version with links, full RSS summaries, draft previews). The RSS digest steps (§15) are a second skill, not code.
- **Trigger B (presence-based):** cron task prepares the briefing early (e.g. 06:00) and uses conditional delivery:
  ```
  name: briefing
  deliver:
    - channel: voice
      followUp: true
      when:
        - state($person = home)
        - state(binary_sensor.kitchen_occupancy = on)
      expires: 11:00 → fallback: push
    - channel: inbox
  ```
  `speech` is spoken on the satellite in the room; `body` (long version) is the task result in the web UI and behind the push deep link. `followUp: true` makes the voice delivery use `start_conversation` instead of `announce`: the satellite listens after the briefing, and the reply turn arrives in a session seeded with the briefing result (voice follow-up, §10), so "tell me more about the second article" is a normal turn. The delivery has priority `normal` and respects quiet hours (§11).
- **Trigger C (on request)** is generic: "good morning" / "what's my briefing" → agent calls `tasks.status("briefing")` (named task), reads the prepared result if fresh, else calls `tasks.run_now("briefing")` (§10). A fresh run usually exceeds the voice latency cap, so the turn backgrounds (§6) and the result arrives through the task's normal deliveries.
- **Weekends:** via goal text only, or two cron tasks with different goals.
- **Language:** the stored user preference (§19). Proactive runs have no request language, so they always use it.
- **Other briefing preferences** (topics, length, what to skip): regular memory `preference` chunks, retrieved like any other memory. No briefing settings anywhere else.

---

## 13. Moonraker (3D printer) — DECIDED
- **Config:** list of printers (`printers: [{ name, url, apiKeySecret }]`, key resolved via `ISecretStore`, §20); one printer for now. Tools take an optional `printer` argument, defaulting to the only one.
- **Commands:** Moonraker REST, `X-Api-Key` auth.
- **Snapshot:** discover via `/server/webcams/list` → `snapshot_url` (served by crowsnest / ustreamer); Marshal fetches the image.

**Tools and risk tiers:**
- **safe:** status, progress, ETA, temperatures, file list, job queue view, print history, snapshot, pause, emergency stop
- **confirm:** start print, resume, cancel, set temperatures, home / move, edit job queue, run macro from allowlist (e.g. `LOAD_FILAMENT`)
- **forbidden:** raw arbitrary G-code, firmware / host restart, file delete

Rationale: pause is reversible and needed by the babysitter without approval. E-stop must act instantly; a false trigger only costs a print + firmware restart. Heaters / start = fire risk → confirm (later option: only when someone is home). The model never writes free G-code; macros cover real use.

### Event subscription
Moonraker websocket (`ws://<host>/websocket`), JSON-RPC 2.0:
1. Connect with API key (`X-Api-Key` header on upgrade, or oneshot token from `/access/oneshot_token` as `?token=`).
2. `server.connection.identify` (client name, version, type, api key).
3. `printer.objects.subscribe` with objects + fields: `print_stats` (state, filename, print_duration, info.current_layer), `virtual_sdcard` (progress), `extruder` / `heater_bed` (temperature, target), `webhooks` (state), `display_status`. Response = initial state.
4. Server pushes `notify_status_update` with **diffs only**.
5. Also: `notify_klippy_ready` / `_shutdown` / `_disconnected`, `notify_history_changed` (job finished: completed / cancelled / error), `notify_job_queue_changed`, `notify_filelist_changed`, `notify_gcode_response`.

`MoonrakerEventSource` (hosted service):
- **State cache** by merging diffs. Status tools and the web UI printer page read the cache → instant answers.
- **Semantic events** derived from transitions (`print_stats.state`: standby → printing = started, → paused, → complete, → error, → cancelled; klippy shutdown) + progress milestones (e.g. every 25 %, first layer done). Only these go on the EventBus; raw temperature diffs never do.
- **Resubscribe** after `notify_klippy_ready`; reconnect with exponential backoff; on reconnect re-read full state so no transition is missed.

### Vision & failure detection
- "How does the print look?" → snapshot + VLM (`vision` capability, §2) → voice answer. Images resized before sending.
- **Failure detection** behind `IPrintFailureDetector`:
  - v1: VLM-based with a **two-strike rule** — two consecutive positive frames before auto-pause.
  - Later: Obico (self-hostable dedicated detector, Moonraker integration) behind the same interface.
- **Print babysitter = system component**, not an agent task: a hosted service in the Moonraker integration, driven by the semantic print events (starts on `started`, stops on `complete` / `cancelled` / `error`), enabled per printer in config. It takes snapshots on an interval, runs the detector, and calls the pause command directly (no agent loop, no guard round trip; pause is safe). It is a system-defined source, so it may send the `critical` notification with the snapshot (§11). The agent can read its state (`printer.status` includes babysitter status and last verdicts) but cannot start, stop or reconfigure it.

---

## 14. Mail — DECIDED (Exchange details DEFERRED)
Accounts: one Gmail, one Microsoft Exchange, one generic IMAP. The Gmail phone app only acts as an IMAP client for non-Google accounts; the Gmail API covers Google accounts only → mail is abstracted.

### Two axes
**Protocol providers (`IMailProvider`):**
- **IMAP (MailKit)** — generic IMAP, Exchange Online (IMAP + XOAUTH2), Gmail (IMAP + XOAUTH2)
- **Microsoft Graph** — native Exchange Online. `Mail.ReadWrite` **without** `Mail.Send` → the scope itself blocks sending.
- **Gmail API** — native threads, labels, Gmail search syntax, History API

**Credential strategies (`ICredentialSource`):** password / app password; OAuth2 Google; OAuth2 Microsoft (Entra). One generic OAuth2 implementation with provider presets. Protocols and credentials combine freely.

**Capability flags** per provider: `NativeThreads`, `Labels`, `ServerSearchSyntax`, `PushEvents`, `ScopeEnforcedNoSend`. Tools adapt (e.g. thread reading falls back to header reconstruction without `NativeThreads`).

### Build order
1. IMAP + all three credential strategies → all three accounts work from the first release.
2. Microsoft Graph provider → scope-enforced no-send for Exchange.
3. Gmail API provider → better threads / search; narrower scopes (`gmail.readonly` + `gmail.compose`) than Gmail over IMAP, which needs full-access `https://mail.google.com/`.

### Account model
`MailAccount { id, owner_id, alias, provider, credential strategy, credential secret name (→ `ISecretStore`, §20), settings }`. Added in the web UI. Aliases ("private", "work", "uni") usable by voice.

### Tools (all safe)
- `mail.search(account?, from, to, subject, text, since, before, unread, folder)` — structured fields translated to Gmail query / IMAP `SEARCH` / Graph `$search` / `$filter`. No account = all accounts.
- `mail.read_message` / `mail.read_thread` — native threads where available; IMAP rebuilds threads from `References` / `In-Reply-To`.
- `mail.list_folders` / `mail.list_labels`
- `mail.create_draft(account, to, subject, body, reply_to?)`, `mail.update_draft(id, ...)`
- Never exposed: send, delete, trash, label / flag changes. Optional later: mark read / archive as confirm tier.

### Drafts
- Gmail API: `drafts.create`. Graph: `POST /me/messages` (creates a draft).
- IMAP: `APPEND` to the drafts folder with the `\Draft` flag; folder found via SPECIAL-USE `\Drafts` (RFC 6154), since names differ per provider / language ("Entwürfe"). Shows up as a normal draft in the phone app.

### No-send enforcement
- IMAP accounts get **no SMTP config at all** → sending structurally impossible.
- Graph: enforced by scope (no `Mail.Send`).
- Gmail: no scope allows drafts without send → enforced in code only; wrapper never calls `messages.send` / `drafts.send`.
- Prompt injection: mail content is untrusted, wrapped as data. Exfiltration via drafts is harmless as long as nothing can send.

### New-mail events
- IMAP: IDLE (server push over a persistent connection, INBOX only, LAN-friendly); polling fallback.
- Gmail API: poll `history.list` since the last `historyId` (Pub/Sub push needs a public endpoint → conflicts with LAN-only).
- Graph: poll delta query (`/me/mailFolders/inbox/messages/delta`); change-notification webhooks also need a public endpoint.

### OAuth setup notes
- Google: own Google Cloud project. In "testing" mode refresh tokens **expire after 7 days** → publish to "production" for personal use (shows an "unverified app" warning, acceptable).
- Microsoft: Entra app registration. Work tenants often disable IMAP and / or block user consent for third-party apps → may need an admin.

DEFERRED: Exchange account = Exchange Online (M365) or on-prem? IMAP enabled? User consent allowed? (Quick test: add the account in Thunderbird with OAuth.)

---

## 15. FreshRSS — DECIDED
- **API:** Google Reader-compatible API (`/api/greader.php`), username + API password (set in FreshRSS profile; stored via `ISecretStore`, §20). Chosen over Fever: categories, labels, read state, `continuation` pagination.
- **Tools:**
  - safe: `rss.list_feeds` / `rss.list_categories`, `rss.get_unread(category?, since?, limit)`, `rss.get_article(id)`, `rss.search`, `rss.mark_read(ids)` (reversible, no confirmation)
  - never exposed: subscribe / unsubscribe / delete
- **Full text:** optional `rss.fetch_full_text(articleId)` via a readability extractor (e.g. SmartReader) — only for items the briefing picks. It takes a FreshRSS item id, never a URL: the server looks up the article's link, so the tool cannot be used to send data to an arbitrary URL (§4).
- **RSS digest** (used by the briefing task, §12): a **skill** (`rss-digest`) the agent follows with the generic tools above. No pipeline code (§1: generic mechanisms only).
  1. Fetch unread since last briefing.
  2. Relevance scoring from titles + feed + category against interests stored as memory preferences, done by a subagent on a cheap template (`agent.delegate("rss-scorer", ...)`, `fast` tier). No batching for now (revisit if volume grows).
  3. Top N: fetch full text; the briefing's own `standard`-tier loop summarizes and clusters into themes.
  4. Output: short voice version (3–5 sentences) + long version (web UI / push) with links.
  5. Mark processed items read afterwards.
- **Learning:** "more like this" / "skip that topic" → preference chunks in memory → better scoring over time.
- **Injection:** article content is untrusted data (same rule as mail).

---

## 16. Calendar — DECIDED
- Small `ICalendarProvider` interface.
- **v1: Home Assistant only.** HA integrates Google Calendar, CalDAV, Outlook / M365, local calendars as `calendar.*` entities. Read via `calendar.get_events`, create via `calendar.create_event`, edit via HA's calendar event update API where the underlying integration supports it (verify API surface when building). No provider code or OAuth in Marshal.
- Limits accepted for v1: only calendars added to HA, fewer fields (no attendees, limited recurrence info).
- **Later:** native providers behind the same interface (CalDAV, Google Calendar API, Microsoft Graph), reusing the mail credential strategies; needed for attendees / free-busy / slot finding.
- **Tiers:** read = safe; create = confirm; edit = confirm; delete = forbidden in v1.

---

## 17. Notes — DECIDED
No shared notes yet; closest thing is Nextcloud. Interfaces allow several backends.

**Interfaces:** `INotesSource` (read) + `IProposalSink` (changes).
- **v1 source:** git repo of Markdown files (local in `/data/notes` or a remote git repo). Obsidian-compatible.
- **Later sources:** Nextcloud via WebDAV (proposals as copies in a `_proposals/` folder, since there is no git), others.

**Read tools (safe):** `notes.list(folder?)`, `notes.read(path)`, `notes.search(query)`.
- **Separate information source, not memory.** Notes are never injected automatically; the agent searches or reads them through the tools when a question needs them. Keeps memory small, curated and in one shape (distilled, English, typed).
- `notes.search` is hybrid (FTS + multilingual embeddings) over the Notes integration's **own index** (`NoteIndexEntry`: owner, path, heading, text, embedding), rebuilt per file on change. Derived data, never exported, rebuildable from the repo.
- **Notes → memory only by distillation:** if a note holds something important (a preference, a procedure, a device fact), the agent writes a normal memory chunk in its own words (`memory.remember`), `source_ref` = note path. No automatic copying.
- Trade-off accepted: a note nobody searches for does not surface on its own; memory covers the important bits.

**Proposals:**
- Agent works in a git worktree on branch `marshal/<slug>`, commits; never touches `main`.
- v1 `IProposalSink`: local branch + web UI diff view with merge / reject / comment. Merge is a user action in the UI, not an agent tool.
- Later: forge sink opening real PRs (Gitea / Forgejo / GitHub).
- Voice: "I put a proposal for your enclosure notes into the review queue".

**Tiers:** read = safe; create proposal = safe; merge = UI only; direct write to main = forbidden.

---

## 18. Web UI — DECIDED
**Stack:** separate SPA + clean public API (REST + **SSE** for live updates; no SignalR, so any HTTP client can consume it). Live updates use one multiplexed stream, `GET /api/v1/events?topics=...` (printer, tasks, approvals, notifications), which also avoids the browser's HTTP/1.1 connection limit; chat replies stream per request. A clean API pays off long-term (mobile, CLI, HA cards, other clients); the HA integration uses the same API. TypeScript API client generated from Marshal's versioned OpenAPI document (contract = single source of truth): `openapi-typescript` turns `/openapi/v1.json` into types at build time, `openapi-fetch` is the small typed `fetch` wrapper. API drift breaks the SPA build, not runtime. SSE event payloads get a small shared hand-written type. Versioning: §6.

**SPA framework — LEANING:** Vue (+ Vite + TypeScript), open to change later.

**Hosting:** standalone at its own URL, served same-origin by Marshal (BFF cookie, §19). HA sidebar panel not planned (iframe breaks OIDC login via `X-Frame-Options` and cross-site cookies); at most a plain sidebar link.

**Mobile — DECIDED:** responsive throughout. Push notifications deep-link into the UI, so these views must work well on a phone: approvals / review queue, task results (incl. long briefing), printer page, notification history, chat.

All roles are in scope (observe, manage, configure, chat); ordering or dropping is decided later.

### Views
1. **Dashboard** (landing page): running tasks + next scheduled runs, pending approvals / reviews (count + quick actions), printer card (state, progress, ETA, latest snapshot), today's cost vs daily cap, recent runs, recent notifications, kill switch. Widgets only, built from existing data.
2. **Chat:** text chat with Marshal (same agent as voice), streaming; rich output: markdown, tables, images / snapshots, diffs.
   - **Images only from Marshal itself.** Model output is untrusted too: a markdown image with an external URL would load on render and leak data in its query string. The SPA's CSP sets `img-src 'self' data:`; the markdown renderer shows images only for same-origin URLs (snapshots, attachments) and turns external image URLs into plain links. Links need a click, which is a user action.
3. **Runs / conversations:** list of all runs (voice / web / task / api), filter by source, outcome, cost. Run detail: context build (active projects + reason, retrieved chunks + scores, skills, template), model calls (tokens, latency, TTFT, cost), tool calls (args, truncated / redacted result, risk tier, guard decision), reply.
4. **Cost:** per turn, task, day, tier; daily cap usage; advisor on its own line.
5. **Memory:** force graph + PCA scatter (granum-style); chunk list, search, editor (create, edit, pin / unpin — the only place pinning happens, supersede, soft delete); history (`ChunkVersion` rows + supersedes chains); audit (stale, conflicts, orphans); projects (description, aliases + alias candidates, correction log).
6. **Tasks:** list + detail (goal, trigger, notes, runs, budget use, result); edit goal (e.g. the briefing), cancel, kill switch; watches; executor sessions (coding) with status. **Task result view** renders markdown — this is where the long briefing lives (no special briefing page).
7. **Review queue:** approvals (frozen action, expiry, approve / reject); notes proposals (diff, merge / reject / comment); coding results (diff + merge).
8. **Notifications history:** every notification with content, priority, channel, outcome (delivered / coalesced / held by quiet hours / expired / fallback) and the reason. Answers "why didn't it tell me?".
9. **Printer page:** live status from the Moonraker state cache (SSE event stream), temperatures (+ chart), progress, ETA, current layer, webcam snapshot / stream (proxied through Marshal so the browser needs no direct printer access and auth stays in one place), job queue, print history, babysitter status (active watch, last detector verdicts).
   - **Controls are UI buttons, not tool calls** — they bypass the model. Same risk tiers: safe actions (pause, e-stop) act immediately; confirm-tier actions (start, resume, cancel, temperatures, move, macros) require a UI confirmation dialog; forbidden actions (raw G-code, restarts, file delete) are not offered — link out to Mainsail / Fluidd for those.
   - UI actions are recorded in the audit log with the actor.
10. **Accounts:** connect Google / Microsoft via OAuth, add IMAP accounts, aliases.
11. **Secrets:** list of database secrets by name, scope (instance / user) and last update; create, replace, delete. Values are **write-only**, never shown again (§20). Secrets from environment / `/run/secrets` are listed read-only with their source, never their value. References from `/config` that do not resolve are flagged.
12. **Config:** skills, templates, MCP servers, risk rules, models, satellites. Config stays file-based and git-friendly → read-only view or editing of the files themselves.
13. **Security:** API tokens (create, revoke, scopes), login providers (read-only).
14. **Preferences:** quiet hours, language, time zone, notification targets, DND.
15. **System:** health, backup status.

---

## 19. Auth & users — DECIDED
### Auth
Pluggable, config-driven. The user runs Authentik → OIDC is primary for them; others can use their own IdP or none.

Principals:
- **Humans (web UI):** BFF pattern. Backend is the OIDC confidential client (authorization code + PKCE), keeps IdP tokens server-side, issues an HttpOnly + Secure + `SameSite=Lax` session cookie. Lax (not Strict) so deep links from push notifications arrive logged in; state-changing requests are protected by ASP.NET Core antiforgery tokens (and are never GET). The SPA knows nothing about OIDC; it only calls `/api/v1/auth/providers`, `/api/v1/auth/login/{provider}`, `/api/v1/auth/logout` and reads the current user from `/api/v1/me`.
- **Machines:** scoped, revocable API tokens (hash stored, created in the UI), always local, independent of the human login provider. Scopes e.g. `conversation:write` (HA integration), `tasks:read`, `admin`. Executors get per-task, single-purpose callback tokens (valid only for that task's callbacks, revoked when the executor session ends).

Provider abstraction (config list, several may be active at once):
- `oidc` — generic: authority, client id / secret, scopes, claim mappings (subject, name, groups). Covers Authentik, Keycloak, Authelia, Pocket ID, Entra, Google.
- `local` — ASP.NET Core Identity, passkeys + password fallback, first-run setup creates admin. For people without an IdP.
- Built on standard ASP.NET Core authentication handlers — no custom crypto.

Internal user model — **one user store for both providers: ASP.NET Core Identity.** `oidc` is the primary login; `local` is the fallback.
- `User` is the Identity user (`IdentityUser<Guid>` + `display_name`). Its key is exposed everywhere else as the strongly typed `OwnerId` (value converter), so no other table knows about Identity.
- OIDC logins are Identity external logins (`AspNetUserLogins`: provider + subject). Match by `sub`, never by email. OIDC users have no password.
- `local` users additionally have a password hash and passkeys (Identity passkey support, .NET 10). The same user can have both an OIDC login and local credentials, e.g. a local admin as break-glass access when the IdP is down.
- Roles are Identity roles. For OIDC users they are re-synced from the group claims on every login.
- Provisioning: role / group mapping from claims (e.g. `marshal-admins` → admin, `marshal-users` → user), else deny. Optional invite flow for `local`.
- Authorization: roles + scopes → ASP.NET policies. Cookie and token principals go through the same policies: a cookie principal carries the scopes its role grants (`user` → all non-admin scopes, `admin` → all scopes), a token principal the scopes it was created with.

Exposure: LAN / VPN only, TLS via reverse proxy. Never exposed directly to the internet (holds mail access, printer control, runs code).

### Users
Single-user instances now (others run their own instance). Multi-user instance (household) later. Prepared without implementing it:
- `owner_id` on every user-owned row: memory chunks, projects / aliases, sessions, tasks, approvals, integration accounts, notification prefs. Shared data later gets an explicit `household` scope column, **not** a null owner: a null `owner_id` already means "instance-scoped" on `Secret` (§20) and must keep only that meaning.
- An execution context flows through everything: `{ OwnerId, Source (voice / web / task / api), Satellite, ... }`. Agent loop, tools and tasks read the user from it, never from a global "the user". Tasks store and run as their owner.
- Identity resolution seam for voice: `IIdentityResolver` (§6). v1 always returns the instance owner.
- Owner-scoped data access: EF Core global query filters on the current user from day one (trivial in v1).
- Integration credentials per account with an owner (e.g. OAuth tokens in DB), not singletons in config.
- Config split: **instance config** (models, MCP servers, risk rules, providers) vs **user preferences** (quiet hours, notification targets, language, time zone) stored per user. The time zone (IANA id) defaults to Home Assistant's configured time zone and drives cron, quiet hours and prompt times (§10, §11). Softer preferences (briefing content, interests) are memory `preference` chunks.
- Notifications target a person (HA `person.*`).
- Authorization policies check ownership (`owner or admin`).
- API: `/api/v1/me` (current user) and `/api/v1/me/...` style routes (versioned like everything else, §6); DTOs carry `owner`.
- System prompt gets the user profile from context, no hardcoded name.
- Audit log records the actor.
- Integration tests seed a second fake user and assert no data leaks between users.
- Explicitly NOT now: user management UI, sharing / permissions between users, speaker identification.

---

## 20. Deployment & config — DECIDED
**Packaging**
- One Docker image for Marshal (the executor service, §10, ships separately); multi-stage build compiles the Vue SPA into `wwwroot`; ASP.NET serves SPA + API same-origin (required by the BFF cookie).
- Example `docker-compose.yml`: Marshal + Ollama (GPU) + optional reverse proxy.
- Monorepo:
  ```
  src/        .NET projects (Api, Abstractions, Core, Memory, Tasks, Integrations.*, Data, ExecutorHost)
  tests/      one test project per src project + Marshal.Testing (shared fakes)
  web/        Vue SPA
  ha/         custom_components/marshal (may move to its own repo, see backlog)
  deploy/     compose, example config
  docs/       design notes, diagrams
  openspec/   specifications and changes
  ```

**Volumes**
- `/config` — human-edited, git-friendly: instance config, `skills/`, `templates/`, `mcp/`, risk rules, `satellites.yaml`. No secrets.
- `/data` — SQLite DB, Data Protection keys, NDJSON memory export, notes repo (v1), snapshots / attachments, backups.

**Base path:** configurable for reverse-proxy setups (`http.pathBase: /marshal`, env `MARSHAL__Http__PathBase`) → `/marshal/api/v1/...`. The API version itself is never configurable (§6).

**Config layering** (low → high): built-in defaults → `/config/marshal.yaml` → env vars (`MARSHAL__Models__Fast__Provider=ollama`). YAML because humans edit it. The environment provider uses the prefix `MARSHAL__` (two underscores) exactly, so secret variables (`MARSHAL_SECRET_*`, one underscore) never enter `IConfiguration`.

**Key naming:** camelCase in every `/config` file (`http.pathBase`, `denyTools`, `modelTier`). It matches the .NET options property names, so binding needs no mapping, and env vars are the same path in PascalCase with `__`. Only exception: `SKILL.md` frontmatter follows the Anthropic Agent Skills format.

**Hot reload:** `/config` file-watched; skills, templates, MCP servers, risk rules, satellites reload without restart. Model and auth settings need a restart.

**Secrets — DECIDED: config never contains a secret, only a secret's name.**
- Small interface `ISecretStore.GetAsync(SecretName, ct) → Result<string>`, two sources:
  - **Environment / files (read-only, operator-managed):** `MARSHAL_SECRET_<NAME>` env vars, or files in `/run/secrets/<name>` (Docker secrets; .NET `AddKeyPerFile`-style, no `*_FILE` convention). For secrets needed at startup: model API keys, OIDC client secret, HA token.
  - **Database (UI-managed):** encrypted with ASP.NET Data Protection, keys in `/data/keys`. Entered in the web UI, **write-only** (never shown again). Owner-scoped; `owner_id` null = instance-scoped (e.g. the printer key). Holds Moonraker / FreshRSS credentials, mail passwords and OAuth refresh tokens.
  - Instance-scoped DB secrets can only be created or changed by admins. Any code path in the instance may use them, but they are never displayed.
  - **Lookup depends on who references the secret:**
    - Names in `/config` (instance-level): environment / files → instance DB rows (`owner_id` null). Never a user's rows.
    - Names on user-owned rows (e.g. `MailAccount`): environment / files → the owner's DB rows → instance DB rows. A user secret overrides an instance secret with the same name.
  - Name mapping: secret `moonraker_voron` = env var `MARSHAL_SECRET_MOONRAKER_VORON` (name upper-cased) = file `/run/secrets/moonraker_voron`.
  - Uniqueness: SQLite treats NULLs as distinct, so a plain `unique (owner_id, name)` would allow duplicate instance names. Two partial unique indexes instead: `unique (name) where owner_id is null` and `unique (owner_id, name) where owner_id is not null`.
  - `Secret` is the one table whose owner query filter also returns null-owner rows (`owner_id = current or owner_id is null`); the lookup order above decides which row wins.
- Config references secrets by name, e.g. `printers: [{ name: voron, url: ..., apiKeySecret: moonraker_voron }]`; `MailAccount` rows reference their secret the same way.
- **Enforcement at startup** (all checks run on every `/config` file, and again on hot reload; a failed reload keeps the previous config):
  - **Reference keys** end in `Secret` (`apiKeySecret`, `clientCredentialSecret`, `tokenSecret`); the part before `Secret` says what the secret holds. The OIDC client secret is therefore referenced as `clientCredentialSecret`, never as a key named `clientSecret` (which reads like a literal). Their value must be a secret *name*: `^[a-z][a-z0-9_]{0,63}$`. Mixed case, dashes and most random strings fail this pattern, but not all: a lowercase hex key (Moonraker API keys are 32 hex characters) can match it, so reference values also go through the value scan below.
  - **Forbidden keys:** the key is split into camelCase words. If the **last word** is `password`, `passphrase`, `token`, `credential` or `credentials`, or the last two words are `api key`, `private key` or `access key`, startup fails. Matching whole words avoids false positives: `maxTokens`, `tokensPerMinute`, `inputPerMTok`, `tokenUrl`, `keyboardLayout` are fine.
  - **Value scan:** any value anywhere that looks like a known credential format (PEM `-----BEGIN`, JWT `eyJ...`, `sk-`, `sk-ant-`, `ghp_`, `xox*-`, hex strings of 32+ characters, base64 / base64url strings of 32+ characters, other long high-entropy strings) fails startup, whatever its key. Secret names are short, lowercase words with underscores, so they never hit these patterns.
  - Options properties marked `[Secret]` can only be bound from `ISecretStore`, never from configuration.
  - **Required secrets** (`[Secret]` properties needed at startup: model API keys, OIDC client secret, HA token) must resolve, otherwise startup fails. References that may be filled later in the UI (printer key, FreshRSS) only produce a health warning and a flag in the Secrets view (§18).
- Losing `/data/keys` makes DB secrets unreadable → keys must be in backups (§21).
- Optional hardening: protect the Data Protection key ring with a certificate supplied via a secret source, so a copy of `/data` alone cannot decrypt credentials.

**Database:** EF Core migrations auto-apply at startup (single instance); DB file copied before each migration.

**Coding executor isolation:** Marshal never gets the Docker socket (= root on host). The executor runs as a **separate small service** (other host / VM, or rootless Docker / Sysbox) that Marshal calls over its API with a scoped token.

**Ops:** `/health` endpoints (unversioned); OpenTelemetry traces + metrics (§21).

---

## 21. Observability, retention, backups — DECIDED
**Captured per run** (voice turn, web chat, task run):
- **Run:** source, owner, session, trigger, start / end, outcome (ok / error / backgrounded (latency cap or `agent.move_to_background`) / cancelled)
- **Context build:** active projects + reason (alias / centroid score), retrieved chunks + scores, loaded skills, template
- **Model calls:** model, tier, input / output / cached tokens, latency, time to first token, cost
- **Tool calls:** tool, args, result (truncated), risk tier, guard decision (safe / pending / confirmed-by-keyword / judge verdict / escalated by taint / denied), run taint flag, duration, error
- **Reply:** final text, `continueConversation`
- UI-initiated and other non-agent actions (printer controls, approvals, merges, token and account changes, kill switch) go to a separate **audit log** (`audit_events`): actor (user / API token / system), action, target, optional run link. Runs cover what the agent did; the audit log covers what humans and machines did outside a run.

**Storage:**
- Domain tables in SQLite (`runs`, `run_steps`, `audit_events`, notification history) — product data queried by the web UI.
- OpenTelemetry in parallel for ops (optional export to Grafana / Tempo / Prometheus).
- One instrumentation layer writes both, behind a small `IRunRecorder` interface.

**Cost:**
- Price table in config per model ($/Mtok input / output / cached); local models = 0.
- Same numbers drive the budgets (§10) → displayed cost = budget-checked cost.

**Retention & privacy** (configurable):
- Full payloads (prompts, tool args / results, replies): **30 days**.
- Metadata (tokens, cost, tool names, durations, outcomes): **1 year**, then deleted or aggregated.
- **Redaction hook** on the recorder: results of sensitive tools (mail, notes) stored as references (message id, path), not content — default on, configurable per tool.
- Memory chunks are not logs; they stay until deleted.
- SQLite not encrypted at rest (host / disk is the protection). Optional later: SQLCipher behind the same data access.

**Backups:**
- Nightly SQLite online backup (`VACUUM INTO`) to `/data/backups`, keep N copies. `system` cron task, no agent, no tokens.
- NDJSON memory export in the same run.
- Includes `/data/keys` (Data Protection) — without it, stored credentials are unreadable after restore.
- `/config` lives in git; off-host backup of `/data/backups` is the operator's responsibility.

---

## 22. Testing — DECIDED
**Workflow: TDD.** Implementation plans are written test-first.

**Rule: tests never need a real LLM.** Anything that cannot be mocked is excluded from the test suite. Models and embeddings are always faked (`IChatClient`, `IEmbeddingGenerator` with deterministic vectors).

1. **Unit tests (xUnit), bulk of the code:**
   - Guard: keyword normalization, frozen action immutable, TTL, nonce, only user actions confirm (never tool output), fail-closed on judge error (judge = fake client), arg-aware risk rules incl. area / floor / domain target expansion (highest tier wins, empty set → confirm), taint escalation of `persists` tools, reason codes on every pending action (taint reason names its sources), confirm-tier denial in subagents and on `denyConfirm` satellites.
   - Approval suspension: run ends `waiting`, server executes the frozen action on approve, resume payload carries decision + result, expiry = rejection.
   - Scheduler: cron, wakeups, delivery `when` conditions / expiry, using .NET `TimeProvider` with a fake clock.
   - Notification router: priorities, quiet hours, override, coalescing, caps.
   - Retrieval scoring: freshness, importance, quotas, supersedes, soft scoping, alias promotion.
   - Ownership: second fake user, no data leakage.
   - Moonraker state machine: replayed websocket traffic, correct semantic events, reconnect gaps.
2. **Harness tests with a scripted fake `IChatClient`** (predefined tool calls / texts): agent loop, max steps, guard interception, tool visibility, auto-background at the latency cap, subagent / advisor calls.
3. **Integration tests (Testcontainers / fakes):**
   - IMAP: GreenMail or Dovecot container (drafts via `APPEND`, IDLE).
   - FreshRSS: official container.
   - HA: fake HTTP / websocket server from recorded responses.
   - Moonraker: fake websocket server with recorded traffic.
   - Gmail / Graph: recorded HTTP responses only; no live accounts.
4. **HA custom integration (Python):** `pytest-homeassistant-custom-component`, Marshal mocked.
5. **Web UI:** component tests with the generated API client mocked (tooling decided with the framework).

**Out of the test suite:** LLM evals (judge accuracy, project inference, tool selection, retrieval quality against real models). Possible later as a separate optional tool (e.g. Microsoft.Extensions.AI.Evaluation), never in CI.

**CI:** any CI; not a design decision. Everything above runs without GPU or API keys (Testcontainers only needs Docker on the runner). Note: HACS installs custom integrations from GitHub repositories, so the HA integration must live in (or be mirrored to) a GitHub repo.

---

## Process
- Everything goes to OpenSpec, but only once the discussion is complete and the user gives the go. Until then: this informal scratchpad.

## Open topics backlog
- **LEANING (fine for now):** Vue (§18)
- **DEFERRED:** Exchange account type / IMAP / consent (§14)
- **LEANING:** HA integration in its own repository (HACS needs `custom_components/<domain>/` + `hacs.json` at the repo root). Decide when the component is built.
- **OPEN (next):** notes proposals in full — write tools, worktree handling, remote repo sync, merge flow (§17). Pushing proposal branches to a remote repo sends data outward → confirm tier or a user action (§4).
- Final review of these notes by the user before OpenSpec formalization
