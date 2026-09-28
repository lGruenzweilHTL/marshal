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
1. Core: agent loop, LLM providers, HA REST endpoint, sessions, tool abstraction, guard, config, machine tokens
2. Extensibility: MCP client (HA MCP first), skills, agent templates
3. Memory
4. Integrations: Moonraker, mail, FreshRSS, calendar, notes
5. Background work: scheduler, tasks, events, executors, notifications
6. Web UI + human auth (added after the web UI discussion)

---

## 2. LLM backend & model usage — DECIDED
Provider-neutral via `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`). Both local (Ollama / llama.cpp) and Claude API.

**Tiers** mapped in config:
- `fast` — voice turns, judge, cheap classification
- `standard` — normal work; subagents for easy tasks use weaker models
- `advisor` — strong model, only consulted for planning advice when actually needed

**Capabilities instead of more tiers:** each configured model has flags `vision`, `tools`, `context_length`. A call requests tier + required capabilities; the router picks the tier's model that has them (e.g. `fast`+`vision` = local Qwen2.5-VL / Gemma 3 via Ollama; `standard`+`vision` = Claude). No separate `vision` tier.

**Advisor limits:**
- No tools. Gets the question + an agent-written context summary (no full transcript). Returns advice text only.
- Max 2 advisor calls per run (configurable per template).
- **Hidden in voice turns** (visibility rule). Voice turns needing planning go through auto-background (§6); the advisor is available there.
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
- **Subagents:** `delegate(template, task)` tool → nested loop with cheaper model
- **Advisor:** `ask_advisor(question)` tool → single strong-model call with trimmed context (§2)

Formats (file-based, git-friendly):
- **Skills:** Anthropic Agent Skills format (`SKILL.md` + frontmatter + files). Progressive disclosure via `load_skill`.
- **Agent templates:** Markdown + frontmatter (like Claude Code subagents): `name`, `description`, `model_tier`, `tools` allowlist, `skills`, prompt body.
- **MCP servers:** config list (stdio + streamable HTTP), with risk rules (§4).

---

## 4. Risk tiers & confirmation guard — DECIDED
**Tiers:** `safe` / `confirm` / `forbidden`. Forbidden capabilities (e.g. mail send) simply don't exist as tools.

**Arg-aware risk rules** (generic risk-engine feature; used for HA, Moonraker macros, any MCP server):
```yaml
risk:
  - match: { server: ha, tool: "*", domain: [lock, alarm_control_panel] }
    tier: confirm
  - match: { server: ha, tool: "*", domain: cover, device_class: [garage, door, gate] }
    tier: confirm
  - match: { server: ha, tool: "*" }
    tier: safe
```
- Tools whose tier depends on args stay visible; the guard decides per call.
- Args that cannot be resolved to a known target (e.g. unknown HA entity name) → confirm (fail closed).

**Flow for `confirm`-tier calls:**
1. Server freezes the pending action: tool, args, `conversation_id`, satellite, nonce, TTL (~60 s). Agent asks the user for confirmation (`continue_conversation` = true).
2. Next user turn:
   - **Exact match** (after normalization: trim, lowercase, strip punctuation; full utterance only) against configured keywords per language (§5) → execute frozen action. Bypasses the model; server-validated only.
   - Otherwise → **small judge LLM** sees only action summary + user reply, temperature 0, structured enum output:
     - `positive` → execute
     - `neutral` (likely topic change) → drop pending action, process message as a normal new turn
     - `negative` → drop, reply "cancelled"
   - Judge timeout / parse error / low confidence → **fail closed** (drop).
3. Executed action is always the frozen one — model cannot alter args after confirmation.

**Rules:**
- Only user turns can confirm. Tool output (mail bodies, RSS, notes) never counts → blocks prompt injection.
- One pending action per conversation; a new one supersedes.
- Background tasks: no user present → confirm-tier actions go to the **approval queue** (actionable push or web UI), same freeze + nonce model.
- Per-satellite policy (`deny_confirm`, `deny_tools`) — §8.
- Any future tool that sends data outward is confirm tier.

---

## 5. Language — DECIDED: German + English
Models and good STT handle both. What actually changes:
- **HA pipeline:** each Assist pipeline has a fixed language (STT, local intents, TTS voice). Options: two pipelines (e.g. one wake word per language), or STT auto-detect where supported. HA-side config, not Marshal.
- **Reply language = request language:** HA sends `language` per turn; Marshal replies in it so the (often monolingual, e.g. Piper) TTS voice matches.
- **Local intents** (timers etc.) only match in the pipeline's language.
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

**Endpoint:** `POST /api/v1/conversation/turns`, machine token with scope `conversation:write`.

Request:
```json
{
  "conversation_id": "01J...",
  "text": "wie weit ist der Druck?",
  "language": "de",
  "device_id": "...",
  "satellite_id": "assist_satellite.kitchen",
  "area_id": "kitchen",
  "ha_user_id": null
}
```

Response: streamed (NDJSON or SSE):
- `delta` events (text chunks)
- `status` events (optional, e.g. "checking printer")
- final event: `{ speech, continue_conversation, conversation_id }`

Rules:
- **Streaming from day one** (HA can stream LLM output into TTS; verify exact HA version / API when building the component). Non-streaming = same stream collected.
- **`continue_conversation`** true when Marshal asks something (confirmations, clarifications) → satellite keeps listening without wake word.
- **Latency budget + auto-background:** target first speech < ~2 s for simple turns; hard cap ~8 s (configurable). At the cap Marshal says "working on it, I'll tell you when done"; the run continues as a background task; result delivered via notifications (§11).
- **HA state access** only via HA MCP / REST. The integration sends no entity dumps.
- **Simple commands never reach Marshal:** "prefer handling commands locally" in the Assist pipeline → HA's built-in intents (lights, timers, ...) handle them first; Marshal is the fallback.

### Sessions and ownership
```
Session
  id                  Marshal-internal id
  client_id           API token/client that created it (HA instance, web, task)
  external_id         HA conversation_id (client-supplied)
  owner_id            resolved on first turn, then pinned
  identity_evidence   { ha_user_id, device_id, satellite_id, area_id }
  resolved_by         owner_default | ha_user_map | satellite_default | ...
  source              voice | web | task | api
```
- Lookup by `(client_id, external_id)`, never by the client-supplied `conversation_id` alone (prevents hijacking by guessed IDs).
- New session → `IIdentityResolver(ha_user_id, device_id, satellite)` → owner, stored with evidence.
- Existing session → pinned owner, no re-resolution per turn.
- Resolver returns a different user on a later turn → new session, never merge.
- Everything created in a session inherits `owner_id` (pending confirmations, tasks, memory writes, approvals).
- Resolver order (future): mapped `ha_user_id` (set for logged-in typed chat, usually null for satellites) → satellite default → instance owner. v1: always instance owner.
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
- **Versioned:** everything clients use — conversation turns, tasks, memory, auth (`/api/v1/auth/*`), SignalR hubs (`/api/v1/hubs/*`).
- **Not versioned:** `/health` (fixed path for Docker / monitoring), SPA routes.
- **Version discovery:** `GET /api/versions` + response headers. The HA integration checks support at setup and shows a clear error in HA if its version is unsupported.
- **Configurable instead:** the base path (§20), e.g. `/marshal` behind a reverse proxy → `/marshal/api/v1/...`.

---

## 7. Home Assistant MCP usage — DECIDED
- HA's MCP Server integration exposes the Assist API as MCP tools (intents like `HassTurnOn`, `HassTurnOff`, `HassLightSet`, `HassClimateSetTemperature`, plus `GetLiveContext` = state of all exposed entities). Streamable HTTP, long-lived token. Only entities exposed to Assist are visible (HA exposure settings = first filter). Tool names / endpoint vary by HA version → verify when building.
- **Risk:** arg-aware rules (§4). Entity resolution via a cached entity registry.
- **Context cost:** `GetLiveContext` cached with short TTL; context builder includes only current-area entities + entities mentioned in the query; model calls the full tool only when needed. HA websocket (event source, §10) keeps the cache fresh.
- **Division of labor:**
  - Model-driven home control → HA MCP.
  - Marshal system calls (announce, push, calendar v1, presence, timers) → HA REST / websocket directly, never through the model.

---

## 8. Per-satellite context — DECIDED
**Per turn, from where it was spoken:**
- **Location:** area + floor from `area_id` (names from HA). "Turn on the light" / "how warm is it here" resolve to this room; HA MCP tools take area arguments.
- **Output mode** derived from the source, not configured per device: voice → plain text, no markdown, short, numbers spoken naturally; text (HA app, web chat) → markdown, links, images.
- **Time & locale:** local time, day of week, request language.
- **Optional satellite profile** in `/config/satellites.yaml` (hot-reloaded); unconfigured satellites use defaults:
  ```yaml
  satellites:
    assist_satellite.kitchen:
      verbosity: short
      notes: "Cooking area. Timer and recipe questions are common."
    assist_satellite.kids_room:
      verbosity: short
      deny_confirm: [moonraker.*, calendar.*]   # confirm-tier tools hidden here
      deny_tools: [mail.*]                      # hidden entirely
  ```

**Implementation:** small `ISatelliteContextProvider(request) → SatelliteContext`. Context builder renders a few lines into the system prompt. Tool filtering applies `deny_tools` / `deny_confirm` (hidden, §1); guard enforces the same policy server-side.

**Not now:** who else is in the room (comes with multi-user); area-specific memory (generic memory covers it, area is part of the turn embedding input).

---

## 9. Memory — DECIDED
Embedding-based semantic memory, inspired by granum (github.com/e-reitbauer/granum), ported to .NET.

Why embeddings: the corpus will outgrow the context over months. Known failure modes and mitigations:
- Silent misses → hybrid search (embeddings + FTS keyword) + explicit `recall` tool for deliberate deep search
- Stale / contradictory facts → `supersedes` edges, freshness decay, periodic audit
- Similarity ≠ identity → deterministic IDs

Taken from granum:
- Typed chunks: `fact`, `preference`, `event`, `procedure`, `person`, `device`
- Deterministic chunk ID = hash(type + normalized title); never similarity-merge
- Edges: `supersedes`, `contradicts`, `depends_on`, `relates_to`; two-phase retrieval (cosine seeds → BFS expansion)
- Score = similarity × importance × freshness; per-type quotas
- Soft deletes; version history via supersedes chains
- Audit: stale / conflicts / orphans
- NDJSON export to git for portability
- Visualizations (force graph, PCA scatter) → web UI memory view (§18)

Changed for Marshal:
- **Automatic retrieval** server-side in the context builder (no model-invoked `query_context` → saves a round trip per voice turn)
- Scopes: `general` + named projects (no git-branch scoping)
- Storage: SQLite (FTS5 + `sqlite-vec`), edges as a table; embeddings via `IEmbeddingGenerator` backed by local Ollama with a **multilingual** model (§5)
- Canonical chunk language: English (§5)
- Not reusing granum directly (Python, Claude-Code-coupled, Kuzu — reportedly archived late 2025, verify)

Layers:
- **Pinned** (always injected in full, kept small) — replaces a separate "general" file
- **General retrieved** — scope `general`, relevance-based
- **Project** — scope `project:<name>`, boosted when project active
- **Notes reference chunks** — read-only, own quota (§17)

Writes: `remember` tool + background consolidation (cheap model reviews finished conversations, proposes chunks, dedupes by ID, flags contradictions). Nightly consolidation is a cron task.

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
7. **Cheap correction:** "not that project" / "that's about X" → unload / switch, negative example, alias learning.
8. **Transparency** in web UI / logs (active projects per turn and why) — not in voice.

---

## 10. Background & independent work — DECIDED
The agent largely controls this itself (e.g. start a coding session and attach a voice update when finished; self-scheduled wakeups; cron jobs). The server enforces limits.

### Task model (durable, SQLite)
`id, owner_id, parent_id, created_by, goal, template, scope, trigger (now | at | cron | event(filter) | after(task)), status (pending | running | waiting | done | failed | cancelled), next_run_at, notes (scratchpad carried between runs), budget (tokens / cost / steps / wall time / runs), deliver (voice(satellite | area | "where I am") | push | briefing | silent), deliver_when (optional event condition), deliver_expires (optional deadline + fallback channel), override_quiet_hours (confirm tier, §11), result`

- **Wakeup** = run ends with `status=waiting` + new trigger. Resume gets goal + notes + trigger payload, **not** the old transcript (no context rot).
- **Conditional delivery:** a finished result can wait for `deliver_when` (e.g. `event(area.kitchen occupancy → on)`). If `deliver_expires` passes first, it goes out via the fallback channel (e.g. push), so results never rot.
- **System tasks** (backups, exports): cron tasks of type `system`, no agent, no tokens.

### Components
- **Scheduler:** own `BackgroundService` + `PeriodicTimer` over `next_run_at`; Cronos for cron parsing (Quartz / Hangfire too heavy); `TimeProvider` for testability
- **EventBus:** in-process `Channel<T>`; adapters normalize to `{source, type, entity, payload}`. Sources: HA websocket, Moonraker websocket, mail (IDLE / polling), executor callbacks
- **TriggerMatcher:** declarative filters, deterministic, no LLM until match
- **Worker pool:** N concurrent headless agent loops
- **Executors:** `IExecutor` for long external work (`Start(spec)` → handle, completion arrives as event)
- **Notifications:** §11
- **Voice follow-up:** `assist_satellite.start_conversation` → announce + listen; the answer can feed the confirmation flow

### Agent tools
`task_create`, `schedule_wakeup`, `watch(event_filter, goal)`, `task_list / task_status / task_cancel`, `delegate(executor, spec, on_done_goal)`, `notify(message, priority, deliver)`

### Guardrails (server-enforced)
- Per-task budget + global daily cost cap; children draw from the parent budget
- Recursion depth limit (~3), fan-out limit
- Minimum wakeup interval (e.g. 5 min, per template)
- Confirm-tier → approval queue
- Kill switch (voice + web UI): stop all background tasks
- Full audit log per run

### Coding executor — DECIDED
Generic, abstract, extendable `IExecutor`. First implementation = Claude Code headless (`claude -p`, stream-json) in a Docker sandbox, repos mounted, git worktree per task, network allowlist. Runs as a **separate executor service** (§20), never with Marshal holding the Docker socket. Executors get one-shot per-task callback tokens. Others plug in later (cloud sessions, Codex, aider, ...).

### Timers & reminders — DECIDED (two paths)
- **Short / basic timers** (cooking etc.): HA built-in timer intents (`HassStartTimer` etc.) via "prefer handling commands locally" — zero tokens. Timers are bound to the satellite device.
- **Long runs / anything needing an agent wakeup** (reminders with context, check-ins, deferred work): Marshal task system (wakeups, cron, watches).

### Use-case ideas
- Print babysitter (snapshot checks, auto-pause on failure, announce on complete) — §13
- Coding delegation (executor in worktree → cheap-model diff review → voice follow-up asking to merge)
- Contextual reminders ("when I get home…" → presence watch)
- Mail triage (event → classify → draft replies → listed in briefing)
- Deferred check-ins ("ask me tomorrow if…" → wakeup + `start_conversation`, answer to memory)
- Self-editable briefing (§12)
- Nightly memory consolidation
- Research → notes proposal + voice summary

---

## 11. Notifications (NotificationRouter) — DECIDED
**Priorities** (set by the sender):
- `critical` — print error, e-stop triggered, spaghetti detected. Always delivered, ignores quiet hours, voice + push. **Only system-defined sources may send `critical`**; the agent's `notify` cannot (otherwise the model could bypass quiet hours).
- `normal` — task done, print complete, approval needed. Respects quiet hours.
- `low` — FYI. Never voice. Goes into the next briefing or a push digest.

**Channel choice:** 1) task's `deliver_when` (§10) → 2) where the user is (HA presence / occupancy): home → voice in the occupied room (`assist_satellite.announce`, or `start_conversation` if a reply is expected); away → push → 3) fallback push. Notifications target a person (HA `person.*`), not a device.

**Quiet hours** (user preference, e.g. 22:00–07:00, day-of-week aware):
- `normal` → silent push or held until quiet hours end (configurable)
- `low` → briefing
- `critical` → delivered anyway
- **Quiet-hour override (confirm tier, optional):** the agent may mark a specific notification / task / watch with `override_quiet_hours` (e.g. "wake me if the print fails tonight"). Confirmed in the conversation when set up, or via the approval queue for self-created tasks. Frozen to that one task / watch, with an expiry.

**Noise control:**
- Coalescing: several `normal` items within ~2 min → one message.
- Per-source limit: e.g. max 1 voice announcement per task per 30 min (push coalesced, not limited).
- Global voice cap: e.g. max 6 unsolicited voice announcements per hour, beyond → push.
- Voice do-not-disturb: "quiet for 2 hours" → temporary quiet period (`critical` still delivers).
- Optional busy detection: media playing in the room / active call → prefer push.

**Actionable push:** HA companion app notifications with buttons (Approve / Reject) → approval queue; reply arrives as `mobile_app_notification_action` event, Marshal verifies the nonce. Push notifications deep-link into the web UI (mobile views, §18).

**History:** every notification is recorded (content, priority, chosen channel, delivered / coalesced / held / expired, reason) → notification history view (§18).

**Interface:** small `INotificationChannel` (voice announce, voice conversation, push, briefing inbox, web UI) — router picks, channels deliver.

---

## 12. Daily briefing — DECIDED
**Rule: a briefing is only a task. No briefing-specific features or code.**

- **Content** lives only in the goal text. The agent uses the normal tools: calendar, mail, RSS pipeline (§15), printer, weather (HA MCP), `task_list` (done / failed / awaiting approval), reminders.
- **Files only:** agent template `briefing.md` (model tier, tool allowlist) + a skill with format guidelines (voice length ~60–90 s, most important points first; long version with links, full RSS summaries, draft previews).
- **Trigger B (presence-based):** cron task prepares the briefing early (e.g. 06:00) and uses conditional delivery:
  ```
  deliver: voice
  deliver_when: event(area.kitchen occupancy → on)
  deliver_expires: 11:00 → fallback: push
  ```
  Voice version on the satellite in the room; long version as task result in the web UI + push. `continue_conversation` stays on after the voice version ("tell me more about the second article").
- **Trigger C (on request)** is generic: "good morning" / "what's my briefing" → agent calls `task_status` on the briefing task, reads the prepared result if fresh, else runs it now.
- **Weekends:** via goal text only, or two cron tasks with different goals.
- **Language:** user preference.

---

## 13. Moonraker (3D printer) — DECIDED
- **Config:** list of printers (`printers: [{ name, url, api_key }]`); one printer for now. Tools take an optional `printer` argument, defaulting to the only one.
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
- Pause is safe → auto-pause in background without approval, followed by a `critical` notification with the snapshot.

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
`MailAccount { id, owner_id, alias, provider, credential strategy, encrypted credentials, settings }`. Added in the web UI. Aliases ("private", "work", "uni") usable by voice.

### Tools (all safe)
- `search_mail(account?, from, to, subject, text, since, before, unread, folder)` — structured fields translated to Gmail query / IMAP `SEARCH` / Graph `$search` / `$filter`. No account = all accounts.
- `read_message` / `read_thread` — native threads where available; IMAP rebuilds threads from `References` / `In-Reply-To`.
- `list_folders` / `list_labels`
- `create_draft(account, to, subject, body, reply_to?)`, `update_draft(id, ...)`
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
- **API:** Google Reader-compatible API (`/api/greader.php`), username + API password (set in FreshRSS profile). Chosen over Fever: categories, labels, read state, `continuation` pagination.
- **Tools:**
  - safe: `list_feeds` / `list_categories`, `get_unread(category?, since?, limit)`, `get_article(id)`, `search`, `mark_read(ids)` (reversible, no confirmation)
  - never exposed: subscribe / unsubscribe / delete
- **Full text:** optional `fetch_full_text(url)` via a readability extractor (e.g. SmartReader) — only for items the briefing picks.
- **Briefing pipeline** (used by the briefing task, §12):
  1. Fetch unread since last briefing.
  2. `fast` model scores relevance from titles + feed + category against interests stored as memory preferences. No batching for now (revisit if volume grows).
  3. Top N: fetch full text; `standard` model summarizes and clusters into themes.
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

**Read tools (safe):** `list_notes(folder?)`, `read_note(path)`, `search_notes(query)`.
- Notes indexed as **read-only reference chunks** in the memory embedding store (like granum spec chunks): never exported, re-indexed on change, own retrieval quota.
- `[[wikilinks]]` become graph edges (memory graph view).

**Proposals:**
- Agent works in a git worktree on branch `marshal/<slug>`, commits; never touches `main`.
- v1 `IProposalSink`: local branch + web UI diff view with merge / reject / comment. Merge is a user action in the UI, not an agent tool.
- Later: forge sink opening real PRs (Gitea / Forgejo / GitHub).
- Voice: "I put a proposal for your enclosure notes into the review queue".

**Tiers:** read = safe; create proposal = safe; merge = UI only; direct write to main = forbidden.

---

## 18. Web UI — DECIDED
**Stack:** separate SPA + clean public API (REST + SignalR / SSE for live updates). A clean API pays off long-term (mobile, CLI, HA cards, other clients); the HA integration uses the same API. TypeScript API client generated from Marshal's versioned OpenAPI document (contract = single source of truth). Versioning: §6.

**SPA framework — LEANING:** Vue (+ Vite + TypeScript), open to change later.

**Hosting:** standalone at its own URL, served same-origin by Marshal (BFF cookie, §19). HA sidebar panel not planned (iframe breaks OIDC login via `X-Frame-Options` and cross-site cookies); at most a plain sidebar link.

**Mobile — DECIDED:** responsive throughout. Push notifications deep-link into the UI, so these views must work well on a phone: approvals / review queue, task results (incl. long briefing), printer page, notification history, chat.

All roles are in scope (observe, manage, configure, chat); ordering or dropping is decided later.

### Views
1. **Dashboard** (landing page): running tasks + next scheduled runs, pending approvals / reviews (count + quick actions), printer card (state, progress, ETA, latest snapshot), today's cost vs daily cap, recent runs, recent notifications, kill switch. Widgets only, built from existing data.
2. **Chat:** text chat with Marshal (same agent as voice), streaming; rich output: markdown, tables, images / snapshots, diffs.
3. **Runs / conversations:** list of all runs (voice / web / task), filter by source, outcome, cost. Run detail: context build (active projects + reason, retrieved chunks + scores, skills, template), model calls (tokens, latency, TTFT, cost), tool calls (args, truncated / redacted result, risk tier, guard decision), reply.
4. **Cost:** per turn, task, day, tier; daily cap usage; advisor on its own line.
5. **Memory:** force graph + PCA scatter (granum-style), notes reference chunks in the graph; chunk list, search, editor (create, edit, pin, supersede, soft delete); history (supersedes chains); audit (stale, conflicts, orphans); projects (description, aliases + alias candidates, correction log).
6. **Tasks:** list + detail (goal, trigger, notes, runs, budget use, result); edit goal (e.g. the briefing), cancel, kill switch; watches; executor sessions (coding) with status. **Task result view** renders markdown — this is where the long briefing lives (no special briefing page).
7. **Review queue:** approvals (frozen action, expiry, approve / reject); notes proposals (diff, merge / reject / comment); coding results (diff + merge).
8. **Notifications history:** every notification with content, priority, channel, outcome (delivered / coalesced / held by quiet hours / expired / fallback) and the reason. Answers "why didn't it tell me?".
9. **Printer page:** live status from the Moonraker state cache (SignalR), temperatures (+ chart), progress, ETA, current layer, webcam snapshot / stream (proxied through Marshal so the browser needs no direct printer access and auth stays in one place), job queue, print history, babysitter status (active watch, last detector verdicts).
   - **Controls are UI buttons, not tool calls** — they bypass the model. Same risk tiers: safe actions (pause, e-stop) act immediately; confirm-tier actions (start, resume, cancel, temperatures, move, macros) require a UI confirmation dialog; forbidden actions (raw G-code, restarts, file delete) are not offered — link out to Mainsail / Fluidd for those.
   - UI actions are recorded in the audit log with the actor.
10. **Accounts:** connect Google / Microsoft via OAuth, add IMAP accounts, aliases.
11. **Config:** skills, templates, MCP servers, risk rules, models, satellites. Config stays file-based and git-friendly → read-only view or editing of the files themselves.
12. **Security:** API tokens (create, revoke, scopes), login providers (read-only).
13. **Preferences:** quiet hours, language, notification targets, DND.
14. **System:** health, backup status.

---

## 19. Auth & users — DECIDED
### Auth
Pluggable, config-driven. The user runs Authentik → OIDC is primary for them; others can use their own IdP or none.

Principals:
- **Humans (web UI):** BFF pattern. Backend is the OIDC confidential client (authorization code + PKCE), keeps IdP tokens server-side, issues an HttpOnly + SameSite=Strict session cookie. The SPA knows nothing about OIDC; it only calls `/api/v1/auth/providers`, `/api/v1/auth/login/{provider}`, `/api/v1/auth/me`, `/api/v1/auth/logout`.
- **Machines:** scoped, revocable API tokens (hash stored, created in the UI), always local, independent of the human login provider. Scopes e.g. `conversation:write` (HA integration), `tasks:read`, `admin`. Executors get one-shot per-task callback tokens.

Provider abstraction (config list, several may be active at once):
- `oidc` — generic: authority, client id / secret, scopes, claim mappings (subject, name, groups). Covers Authentik, Keycloak, Authelia, Pocket ID, Entra, Google.
- `local` — ASP.NET Core Identity, passkeys + password fallback, first-run setup creates admin. For people without an IdP.
- Built on standard ASP.NET Core authentication handlers — no custom crypto.

Internal user model, decoupled from the provider:
- `User { id, display_name, roles }` + `ExternalLogin { provider, subject }`. Match by `sub`, never by email.
- Provisioning: role / group mapping from claims (e.g. `marshal-admins` → admin, `marshal-users` → user), else deny. Optional invite flow for `local`.
- Authorization: roles + scopes → ASP.NET policies. Cookie and token principals go through the same policies.

Exposure: LAN / VPN only, TLS via reverse proxy. Never exposed directly to the internet (holds mail access, printer control, runs code).

### Users
Single-user instances now (others run their own instance). Multi-user instance (household) later. Prepared without implementing it:
- `owner_id` on every user-owned row: memory chunks, projects / aliases, sessions, tasks, approvals, integration accounts, notification prefs. Nullable owner (or explicit `household` scope) reserved for shared data later.
- An execution context flows through everything: `{ UserId, Source (voice / web / task / api), Satellite, ... }`. Agent loop, tools and tasks read the user from it, never from a global "the user". Tasks store and run as their owner.
- Identity resolution seam for voice: `IIdentityResolver` (§6). v1 always returns the instance owner.
- Owner-scoped data access: EF Core global query filters on the current user from day one (trivial in v1).
- Integration credentials per account with an owner (e.g. OAuth tokens in DB), not singletons in config.
- Config split: **instance config** (models, MCP servers, risk rules, providers) vs **user preferences** (briefing, quiet hours, notification targets, language) stored per user.
- Notifications target a person (HA `person.*`).
- Authorization policies check ownership (`owner or admin`).
- API: `/api/me/...` style routes; DTOs carry `owner`.
- System prompt gets the user profile from context, no hardcoded name.
- Audit log records the actor.
- Integration tests seed a second fake user and assert no data leaks between users.
- Explicitly NOT now: user management UI, sharing / permissions between users, speaker identification.

---

## 20. Deployment & config — DECIDED
**Packaging**
- One Docker image; multi-stage build compiles the Vue SPA into `wwwroot`; ASP.NET serves SPA + API same-origin (required by the BFF cookie).
- Example `docker-compose.yml`: Marshal + Ollama (GPU) + optional reverse proxy.
- Monorepo:
  ```
  src/        .NET projects (Api, Core/Harness, Memory, Tasks, Integrations.*)
  web/        Vue SPA
  ha/         custom_components/marshal (HACS-installable)
  deploy/     compose, example config
  ```

**Volumes**
- `/config` — human-edited, git-friendly: instance config, `skills/`, `templates/`, `mcp/`, risk rules, `satellites.yaml`. No secrets.
- `/data` — SQLite DB, Data Protection keys, NDJSON memory export, notes repo (v1), snapshots / attachments, backups.

**Base path:** configurable for reverse-proxy setups (`http.path_base: /marshal`, env `MARSHAL__Http__PathBase`) → `/marshal/api/v1/...`. The API version itself is never configurable (§6).

**Config layering** (low → high): built-in defaults → `/config/marshal.yaml` → env vars (`MARSHAL__Models__Fast__Provider=ollama`) → Docker secrets / `*_FILE` env vars. YAML because humans edit it.

**Hot reload:** `/config` file-watched; skills, templates, MCP servers, risk rules, satellites reload without restart. Model and auth settings need a restart.

**Secrets & credentials**
- Static secrets (API keys, OIDC client secret): env / Docker secrets.
- Runtime credentials (OAuth refresh tokens, IMAP passwords, per owner): DB, encrypted with ASP.NET Data Protection; keys in `/data/keys`. Losing the keys makes the credentials unreadable → must be in backups.

**Database:** EF Core migrations auto-apply at startup (single instance); DB file copied before each migration.

**Coding executor isolation:** Marshal never gets the Docker socket (= root on host). The executor runs as a **separate small service** (other host / VM, or rootless Docker / Sysbox) that Marshal calls over its API with a scoped token.

**Ops:** `/health` endpoints (unversioned); OpenTelemetry traces + metrics (§21).

---

## 21. Observability, retention, backups — DECIDED
**Captured per run** (voice turn, web chat, task run):
- **Run:** source, owner, session, trigger, start / end, outcome (ok / error / auto-backgrounded / cancelled)
- **Context build:** active projects + reason (alias / centroid score), retrieved chunks + scores, loaded skills, template
- **Model calls:** model, tier, input / output / cached tokens, latency, time to first token, cost
- **Tool calls:** tool, args, result (truncated), risk tier, guard decision (safe / pending / confirmed-by-keyword / judge verdict / denied), duration, error
- **Reply:** final text, `continue_conversation`
- UI-initiated actions (e.g. printer controls) with the actor.

**Storage:**
- Domain tables in SQLite (`runs`, `run_steps`, notification history) — product data queried by the web UI.
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
   - Guard: keyword normalization, frozen action immutable, TTL, nonce, only user turns confirm, fail-closed on judge error (judge = fake client), arg-aware risk rules.
   - Scheduler: cron, wakeups, `deliver_when` / expiry, using .NET `TimeProvider` with a fake clock.
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
- Final review of these notes by the user before OpenSpec formalization
